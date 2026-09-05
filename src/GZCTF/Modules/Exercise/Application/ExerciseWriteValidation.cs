using GZCTF.Modules.Exercise.Contracts;

namespace GZCTF.Modules.Exercise.Application;

internal static class ExerciseWriteValidation
{
    public static void Validate(ExerciseCreateModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title) || model.Title.Length > 256)
            throw Invalid("exercise_title_invalid", "Exercise title must contain between 1 and 256 characters.");
        if (model.Content is null || model.Content.Length > 1_000_000)
            throw Invalid("exercise_content_invalid", "Exercise content cannot exceed 1,000,000 characters.");
        if (!Enum.IsDefined(model.Category) || !Enum.IsDefined(model.Type) ||
            !Enum.IsDefined(model.Difficulty) || !Enum.IsDefined(model.Environment) ||
            (model.NetworkMode.HasValue && !Enum.IsDefined(model.NetworkMode.Value)))
            throw Invalid("exercise_enum_invalid", "Exercise category, type, difficulty, or runtime enum is invalid.");
        if (model.Hints is { Count: > 100 } ||
            model.Hints?.Any(hint => hint is null || hint.Length > 4096) == true)
            throw Invalid("exercise_hints_invalid", "An exercise may contain at most 100 hints of 4,096 characters each.");
        if (model.Tags is { Count: > 100 } || model.Tags?.Any(tag => tag is null || tag.Length > 256) == true)
            throw Invalid("exercise_tags_invalid", "An exercise may contain at most 100 tags of 256 characters each.");
        ValidateRuntime(model.Type, model.Environment, model.ContainerImage, model.ImageTemplateId,
            model.ExposePort, model.MemoryLimit, model.StorageLimit, model.CPUCount, model.FlagTemplate);
        if (model.Flags is { Count: > 100 } || model.Flags?.Any(flag =>
                flag is null || string.IsNullOrWhiteSpace(flag.Flag) || flag.Flag.Length > Limits.MaxFlagLength ||
                !Enum.IsDefined(flag.ScoreMode) || !Enum.IsDefined(flag.AnswerType) ||
                flag.MaxAttempts < 0 || flag.FixedScore < 0) == true)
            throw Invalid("exercise_flags_invalid", "Exercise flags contain invalid values or exceed the limit of 100.");
        ValidateAttachment(model.Attachment);
        foreach (var flag in model.Flags ?? [])
            ValidateAttachment(flag.Attachment);
    }

    public static void ValidateRuntime(ChallengeType type, EnvironmentType environment, string? containerImage,
        int? imageTemplateId, int? exposePort, int? memoryLimit, int? storageLimit, int? cpuCount,
        string? flagTemplate)
    {
        if (!Enum.IsDefined(type) || !Enum.IsDefined(environment))
            throw Invalid("exercise_enum_invalid", "Exercise type or environment is invalid.");
        if (environment == EnvironmentType.WindowsVM)
            throw Invalid("exercise_environment_unsupported", "Public exercises currently support Docker runtimes only.");
        if (!type.IsContainer())
        {
            if (imageTemplateId.HasValue || environment != EnvironmentType.None)
                throw Invalid("exercise_runtime_invalid", "Attachment exercises cannot bind a runtime template.");
            return;
        }
        if (imageTemplateId is <= 0 || (imageTemplateId is null && string.IsNullOrWhiteSpace(containerImage)))
            throw Invalid("exercise_image_required", "Container exercises require an image or a ready Docker template.");
        if (containerImage?.Length > 512)
            throw Invalid("exercise_image_invalid", "The container image reference is too long.");
        if (exposePort is not (>= 1 and <= 65535))
            throw Invalid("exercise_port_invalid", "Container exercises require exposePort between 1 and 65535.");
        if (memoryLimit is <= 0 || storageLimit is <= 0 || cpuCount is <= 0)
            throw Invalid("exercise_resource_limit_invalid", "Container resource limits must be positive when specified.");
        if (type == ChallengeType.DynamicContainer &&
            (string.IsNullOrWhiteSpace(flagTemplate) || !new DynamicFlagGenerator(flagTemplate).IsValid()))
            throw Invalid("exercise_flag_template_invalid", "Dynamic containers require a valid flagTemplate.");
    }

    public static void ValidateAttachment(ExerciseOpenApiAttachmentModel? attachment)
    {
        if (attachment is null)
            return;
        var local = !string.IsNullOrWhiteSpace(attachment.FileHash);
        var remote = !string.IsNullOrWhiteSpace(attachment.RemoteUrl);
        if (local == remote)
            throw Invalid("exercise_attachment_invalid", "Specify exactly one of fileHash or remoteUrl.");
        if (local && (attachment.FileHash!.Length != 64 || !attachment.FileHash.All(Uri.IsHexDigit)))
            throw Invalid("exercise_attachment_hash_invalid", "Local attachments require a SHA-256 fileHash.");
        if (remote && (attachment.RemoteUrl.Length > 2048 ||
                       !Uri.TryCreate(attachment.RemoteUrl, UriKind.Absolute, out var uri) ||
                       uri.Scheme is not ("http" or "https")))
            throw Invalid("exercise_attachment_url_invalid", "Remote attachments require an absolute HTTP or HTTPS URL.");
    }

    static ExerciseApiContractException Invalid(string code, string detail) => new(code, detail, 422);
}
