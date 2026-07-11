using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GZCTF.Modules.Ctf.Infrastructure.Persistence;

public sealed class GameOwnershipEntityConfiguration : IEntityTypeConfiguration<Game>
{
    public void Configure(EntityTypeBuilder<Game> builder)
    {
        builder.HasIndex(game => game.OwnerId);
        builder.HasOne(game => game.Owner)
            .WithMany()
            .HasForeignKey(game => game.OwnerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
