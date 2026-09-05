using System.Security.Cryptography;
using GZCTF.Repositories.Interface;
using GZCTF.Storage.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Processing;

namespace GZCTF.Repositories;

public class BlobRepository(AppDbContext context, ILogger<BlobRepository> logger, IBlobStorage storage)
    : RepositoryBase(context), IBlobRepository
{
    public override Task<int> CountAsync(CancellationToken token = default) =>
        Context.Files.CountAsync(token);

    public async Task<LocalFile> CreateOrUpdateBlob(IFormFile file, string? fileName = null,
        CancellationToken token = default)
    {
        await using var tmp = BufferHelper.GetTempStream(file.Length);

        logger.SystemLog(
            StaticLocalizer[nameof(Resources.Program.FileRepository_CacheLocation),
                tmp.GetType()], TaskStatus.Pending,
            LogLevel.Trace);

        await file.CopyToAsync(tmp, token);
        return await StoreBlob(fileName ?? file.FileName, tmp, token);
    }

    public Task<LocalFile> CreateOrUpdateBlobFromStream(string fileName, Stream stream,
        CancellationToken token = default) =>
        StoreBlob(fileName, stream, token);

    public async Task<LocalFile?> IncrementBlobReference(string fileHash, CancellationToken token = default)
    {
        await using var transaction = await BeginBlobTransactionAsync(token);
        await LockBlobAsync(fileHash, token);
        var localFile = await GetBlobByHash(fileHash, token);

        if (localFile is null)
            return null;

        if (Context.Database.IsRelational())
            await Context.Entry(localFile).ReloadAsync(token);

        localFile.ReferenceCount++;
        localFile.UploadTimeUtc = DateTimeOffset.UtcNow; // update upload time

        logger.SystemLog(
            StaticLocalizer[nameof(Resources.Program.FileRepository_ReferenceCounting),
                localFile.Hash[..8], localFile.Name, localFile.ReferenceCount],
            TaskStatus.Success, LogLevel.Debug);

        Context.Update(localFile);
        await SaveAsync(token);
        if (transaction is not null)
            await transaction.CommitAsync(token);

        return localFile;
    }

    public async Task<LocalFile?> CreateOrUpdateImage(IFormFile file, string fileName,
        int resize = 300,
        CancellationToken token = default)
    {
        // we do not process images larger than 32MB
        if (file.Length >= 32 * 1024 * 1024)
            return null;

        try
        {
            await using var webpStream = BufferHelper.GetTempStream(8192, "image");
            await using (var tmp = BufferHelper.GetTempStream(file.Length))
            {
                await file.CopyToAsync(tmp, token);
                tmp.Position = 0;
                using var image = await Image.LoadAsync(tmp, token);

                if (image.Metadata.DecodedImageFormat is GifFormat)
                    return await StoreBlob($"{fileName}.gif", tmp, token);

                if (resize > 0)
                    image.Mutate(im => im.Resize(resize, 0));

                await image.SaveAsWebpAsync(webpStream, token);
            }

            return await StoreBlob($"{fileName}.webp", webpStream, token);
        }
        catch
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.FileRepository_ImageSaveFailed),
                    file.Name],
                TaskStatus.Failed, LogLevel.Warning);
            return null;
        }
    }

    public async Task<TaskStatus> DeleteBlob(LocalFile file, CancellationToken token = default)
    {
        var hasAmbientTransaction = Context.Database.CurrentTransaction is not null;
        {
            await using var transaction = await BeginBlobTransactionAsync(token);
            await LockBlobAsync(file.Hash, token);
            if (Context.Database.IsRelational())
            {
                await Context.Entry(file).ReloadAsync(token);
                if (Context.Entry(file).State == EntityState.Detached)
                    return TaskStatus.NotFound;
            }

            // A relation update may still roll back. Never remove its bytes before
            // commit, or trust a legacy counter over real business references.
            if (file.ReferenceCount > 1 || hasAmbientTransaction || await HasReferencesAsync(file, token))
            {
                if (file.ReferenceCount > 0)
                    file.ReferenceCount--;
                await SaveAsync(token);
                if (transaction is not null)
                    await transaction.CommitAsync(token);
                return TaskStatus.Success;
            }

            if (transaction is not null)
                await transaction.CommitAsync(token);
        }

        return await DeleteUnreferencedBlobByHash(file.Hash, token);
    }

    public async Task<TaskStatus> DeleteUnreferencedBlobByHash(string fileHash, CancellationToken token = default)
    {
        if (Context.Database.CurrentTransaction is not null)
            return TaskStatus.Denied;

        if (!Context.Database.IsNpgsql())
            return await DeleteIdleBlobAsync(fileHash, token);

        await Context.Database.OpenConnectionAsync(token);
        try
        {
            // Keep the hash locked across the database commit and object deletion so
            // a simultaneous upload cannot recreate bytes just before we remove them.
            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_lock(hashtextextended({"blob:" + fileHash}, 0))", token);
            try
            {
                return await DeleteIdleBlobAsync(fileHash, token);
            }
            finally
            {
                await Context.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_unlock(hashtextextended({"blob:" + fileHash}, 0))",
                    CancellationToken.None);
            }
        }
        finally
        {
            await Context.Database.CloseConnectionAsync();
        }
    }

    async Task<TaskStatus> DeleteIdleBlobAsync(string fileHash, CancellationToken token)
    {
        await using var transaction = await BeginBlobTransactionAsync(token);
        await LockBlobAsync(fileHash, token);
        if (Context.Database.IsNpgsql())
        {
            // A row lock excludes concurrent FK insertion without locking unrelated
            // files. Hash-only binders share the blob transaction until their save.
            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"Files\" WHERE \"Hash\" = {fileHash} FOR UPDATE", token);
        }

        var file = await GetBlobByHash(fileHash, token);
        if (file is null)
            return TaskStatus.NotFound;
        if (await HasReferencesAsync(file, token))
            return TaskStatus.Denied;

        var path = StoragePath.Combine(PathHelper.Uploads, file.Location, file.Hash);

        // check if file exists
        if (!await storage.ExistsAsync(path, token))
        {
            Context.Files.Remove(file);
            await SaveAsync(token);
            if (transaction is not null)
                await transaction.CommitAsync(token);
            return TaskStatus.NotFound;
        }

        // Commit metadata first. If the transaction fails, the original bytes are
        // untouched. A storage failure afterwards leaves an unreferenced object,
        // never a surviving business reference pointing at missing bytes.
        Context.Files.Remove(file);
        await SaveAsync(token);
        if (transaction is not null)
            await transaction.CommitAsync(token);

        try
        {
            await storage.DeleteAsync(path, token);
        }
        catch (Exception e)
        {
            // log the exception and return failed
            logger.LogErrorMessage(e, StaticLocalizer[
                nameof(Resources.Program.FileRepository_DeleteFile),
                file.Hash[..8], file.Name]);

            return TaskStatus.Failed;
        }

        // log success
        logger.SystemLog(StaticLocalizer[
                nameof(Resources.Program.FileRepository_DeleteFile),
                file.Hash[..8], file.Name],
            TaskStatus.Success, LogLevel.Information);

        return TaskStatus.Success;
    }

    public async Task<TaskStatus> DeleteBlobByHash(string fileHash,
        CancellationToken token = default)
    {
        var file = await GetBlobByHash(fileHash, token);

        if (file is null)
            return TaskStatus.NotFound;

        return await DeleteBlob(file, token);
    }

    public Task<LocalFile?> GetBlobByHash(string? fileHash, CancellationToken token = default)
    {
        if (fileHash is null)
            return Task.FromResult<LocalFile?>(null);

        return Context.Files.SingleOrDefaultAsync(e => e.Hash == fileHash, token);
    }

    public Task<LocalFile[]> GetBlobs(int count, int skip, CancellationToken token = default) =>
        Context.Files.OrderBy(e => e.Name).Skip(skip).Take(count).ToArrayAsync(token);

    public async Task DeleteAttachment(Attachment? attachment, CancellationToken token = default)
    {
        switch (attachment)
        {
            case null:
                return;
            case { Type: FileType.Local, LocalFile: not null }:
                await DeleteBlob(attachment.LocalFile, token);
                break;
        }

        Context.Remove(attachment);
    }

    private async Task<LocalFile> StoreBlob(string fileName, Stream contentStream,
        CancellationToken token = default)
    {
        contentStream.Position = 0;
        var hash = await SHA256.HashDataAsync(contentStream, token);
        var fileHash = Convert.ToHexStringLower(hash);

        await using var transaction = await BeginBlobTransactionAsync(token);
        await LockBlobAsync(fileHash, token);

        var localFile = await GetBlobByHash(fileHash, token);

        if (localFile is not null)
        {
            if (Context.Database.IsRelational())
                await Context.Entry(localFile).ReloadAsync(token);
            localFile.FileSize = contentStream.Length;
            localFile.Name = fileName; // allow to rename
            localFile.UploadTimeUtc = DateTimeOffset.UtcNow; // update upload time
            localFile.ReferenceCount++; // same hash, add ref count

            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.FileRepository_ReferenceCounting),
                    localFile.Hash[..8], localFile.Name,
                    localFile.ReferenceCount],
                TaskStatus.Success, LogLevel.Debug);

            Context.Update(localFile);
        }
        else
        {
            localFile = new() { Hash = fileHash, Name = fileName, FileSize = contentStream.Length };
            await Context.AddAsync(localFile, token);
        }

        var path = StoragePath.Combine(PathHelper.Uploads, localFile.Location, localFile.Hash);

        contentStream.Position = 0;
        await storage.WriteAsync(path, contentStream, false, token);

        await SaveAsync(token);
        if (transaction is not null)
            await transaction.CommitAsync(token);
        return localFile;
    }

    async Task<IDbContextTransaction?> BeginBlobTransactionAsync(CancellationToken token) =>
        Context.Database.IsRelational() && Context.Database.CurrentTransaction is null
            ? await Context.Database.BeginTransactionAsync(token)
            : null;

    async Task LockBlobAsync(string hash, CancellationToken token)
    {
        if (Context.Database.IsNpgsql())
            await Context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({"blob:" + hash}, 0))", token);
    }

    async Task<bool> HasReferencesAsync(LocalFile file, CancellationToken token)
    {
        Context.ChangeTracker.DetectChanges();
        foreach (var entry in Context.ChangeTracker.Entries()
                     .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var foreignKey in entry.Metadata.GetForeignKeys()
                         .Where(key => key.PrincipalEntityType.ClrType == typeof(LocalFile)))
                if (foreignKey.Properties.Count == 1 &&
                    Equals(entry.Property(foreignKey.Properties[0].Name).CurrentValue, file.Id))
                    return true;

            if (entry.Entity is Game game && game.PosterHash == file.Hash ||
                entry.Entity is TrainingCourse course && course.CoverFileHash == file.Hash ||
                entry.Entity is Team team && team.AvatarHash == file.Hash ||
                entry.Entity is UserInfo user && user.AvatarHash == file.Hash)
                return true;
        }

        return await Context.Attachments.AnyAsync(item => item.LocalFileId == file.Id, token) ||
            await Context.TrainingCourseResources.AnyAsync(item => item.LocalFileId == file.Id, token) ||
            await Context.TrainingCourseChapters.AnyAsync(item => item.VideoFileId == file.Id, token) ||
            await Context.TrainingCourses.AnyAsync(item => item.CoverFileHash == file.Hash, token) ||
            await Context.Participations.AnyAsync(item => item.Writeup != null && item.Writeup.Id == file.Id, token) ||
            await Context.Games.AnyAsync(item => item.PosterHash == file.Hash, token) ||
            await Context.Teams.AnyAsync(item => item.AvatarHash == file.Hash, token) ||
            await Context.Users.AnyAsync(item => item.AvatarHash == file.Hash, token);
    }
}
