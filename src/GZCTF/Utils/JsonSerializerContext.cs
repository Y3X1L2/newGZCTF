using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using GZCTF.Models.Internal;
using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Ctf.Contracts;
using GZCTF.Modules.Penetration.Contracts;
using GZCTF.Models.Request.Account;
using GZCTF.Models.Request.Admin;
using GZCTF.Models.Request.Edit;
using GZCTF.Models.Request.Game;
using GZCTF.Models.Request.Info;
using GZCTF.Models.Request.Training;
using GZCTF.Services.Container.Provider;
using GZCTF.Services.TeamLab;
using Namotion.Reflection;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Generation.TypeMappers;

namespace GZCTF.Utils;

[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateOnly))]
[JsonSerializable(typeof(TaskStatus))]
[JsonSerializable(typeof(AnswerResult))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<int>))]
[JsonSerializable(typeof(List<ImageDistributionReference>))]
[JsonSerializable(typeof(HashSet<string>))]
[JsonSerializable(typeof(DockerRegistryOptions))]
[JsonSerializable(typeof(GameMetadata))]
[JsonSerializable(typeof(RequestResponse))]
[JsonSerializable(typeof(RequestResponse<RegisterStatus>))]
[JsonSerializable(typeof(RequestResponse<bool>))]
[JsonSerializable(typeof(ProfileUserInfoModel))]
[JsonSerializable(typeof(ConfigEditModel))]
[JsonSerializable(typeof(ArrayResponse<UserInfoModel>))]
[JsonSerializable(typeof(ArrayResponse<TeamInfoModel>))]
[JsonSerializable(typeof(TeamJoinRequestCreateModel))]
[JsonSerializable(typeof(TeamJoinRequestReviewModel))]
[JsonSerializable(typeof(TeamJoinRequestModel))]
[JsonSerializable(typeof(TeamJoinRequestModel[]))]
[JsonSerializable(typeof(StudentGroupEditModel))]
[JsonSerializable(typeof(StudentGroupMemberEditModel))]
[JsonSerializable(typeof(StudentGroupManagerEditModel))]
[JsonSerializable(typeof(StudentGroupBriefModel))]
[JsonSerializable(typeof(StudentGroupBriefModel[]))]
[JsonSerializable(typeof(StudentGroupDetailModel))]
[JsonSerializable(typeof(FlagStepInfo))]
[JsonSerializable(typeof(TrainingCourseEditModel))]
[JsonSerializable(typeof(TrainingCourseEnrollmentApplyModel))]
[JsonSerializable(typeof(TrainingCourseEnrollmentReviewModel))]
[JsonSerializable(typeof(TrainingCourseTeacherEditModel))]
[JsonSerializable(typeof(TrainingCourseChapterEditModel))]
[JsonSerializable(typeof(TrainingCourseResourceEditModel))]
[JsonSerializable(typeof(TrainingCourseChallengeEditModel))]
[JsonSerializable(typeof(TrainingCourseChallengeUpdateModel))]
[JsonSerializable(typeof(TrainingCourseImageTemplateAttachModel))]
[JsonSerializable(typeof(TrainingCourseDockerRegisterModel))]
[JsonSerializable(typeof(TrainingCourseLocalImageImportModel))]
[JsonSerializable(typeof(TrainingCourseChallengeCreateModel))]
[JsonSerializable(typeof(TrainingCourseChallengeEditDetailModel))]
[JsonSerializable(typeof(TrainingCourseTheoryQuestionModel))]
[JsonSerializable(typeof(TrainingCourseTheoryQuestionModel[]))]
[JsonSerializable(typeof(TrainingCourseTheoryPaperQuestionEditModel[]))]
[JsonSerializable(typeof(TrainingCourseChapterTheoryPaperEditModel))]
[JsonSerializable(typeof(TrainingCourseChapterTheoryPaperDetailModel))]
[JsonSerializable(typeof(TrainingCourseChapterTheorySummaryModel))]
[JsonSerializable(typeof(TrainingCourseChapterTheorySummaryModel[]))]
[JsonSerializable(typeof(TrainingCourseChapterTheoryPlayerPaperModel))]
[JsonSerializable(typeof(TrainingCourseModel))]
[JsonSerializable(typeof(TrainingCourseModel[]))]
[JsonSerializable(typeof(TrainingCourseTeacherModel))]
[JsonSerializable(typeof(TrainingCourseTeacherModel[]))]
[JsonSerializable(typeof(TrainingCourseEnrollmentModel))]
[JsonSerializable(typeof(TrainingCourseEnrollmentModel[]))]
[JsonSerializable(typeof(TrainingCourseResourceModel))]
[JsonSerializable(typeof(TrainingCourseResourceModel[]))]
[JsonSerializable(typeof(TrainingCourseChapterModel))]
[JsonSerializable(typeof(TrainingCourseChapterModel[]))]
[JsonSerializable(typeof(TrainingCourseChallengeModel))]
[JsonSerializable(typeof(TrainingCourseChallengeModel[]))]
[JsonSerializable(typeof(TrainingCourseImageTemplateModel))]
[JsonSerializable(typeof(TrainingCourseImageTemplateModel[]))]
[JsonSerializable(typeof(TrainingCourseChallengeDetailModel))]
[JsonSerializable(typeof(TrainingCourseSubmitResultModel))]
[JsonSerializable(typeof(TrainingCheckInModel))]
[JsonSerializable(typeof(TrainingCheckInModel[]))]
[JsonSerializable(typeof(TrainingActivityPointModel))]
[JsonSerializable(typeof(TrainingActivityPointModel[]))]
[JsonSerializable(typeof(TrainingPersonalOverviewModel))]
[JsonSerializable(typeof(LogMessageModel[]))]
[JsonSerializable(typeof(WriteupInfoModel[]))]
[JsonSerializable(typeof(ArrayResponse<ContainerInstanceModel>))]
[JsonSerializable(typeof(ArrayResponse<LocalFile>))]
[JsonSerializable(typeof(List<LocalFile>))]
[JsonSerializable(typeof(PostDetailModel))]
[JsonSerializable(typeof(GameInfoModel))]
[JsonSerializable(typeof(ArrayResponse<GameInfoModel>))]
[JsonSerializable(typeof(GameNotice))]
[JsonSerializable(typeof(GameNotice[]))]
[JsonSerializable(typeof(ChallengeEditDetailModel))]
[JsonSerializable(typeof(ChallengeInfoModel[]))]
[JsonSerializable(typeof(ContainerInfoModel))]
[JsonSerializable(typeof(BasicGameInfoModel[]))]
[JsonSerializable(typeof(DetailedGameInfoModel))]
[JsonSerializable(typeof(ScoreboardModel))]
[JsonSerializable(typeof(TheoryQuestionBankItemModel))]
[JsonSerializable(typeof(TheoryQuestionBankItemModel[]))]
[JsonSerializable(typeof(TheoryPaperDetailModel))]
[JsonSerializable(typeof(TheoryPlayerPaperModel))]
[JsonSerializable(typeof(TheoryAnswerSheetEditModel))]
[JsonSerializable(typeof(TheoryAnswerSheetSummaryModel))]
[JsonSerializable(typeof(TheoryResultsModel))]
[JsonSerializable(typeof(TheoryScoreboardItemModel[]))]
[JsonSerializable(typeof(AwdpServiceCreateModel))]
[JsonSerializable(typeof(AwdpServiceUpdateModel))]
[JsonSerializable(typeof(AwdpServiceViewModel))]
[JsonSerializable(typeof(AwdpServiceViewModel[]))]
[JsonSerializable(typeof(AwdpSubmitModel))]
[JsonSerializable(typeof(AwdpSubmitResultModel))]
[JsonSerializable(typeof(AwdpGameStatusModel))]
[JsonSerializable(typeof(AwdpTeamServiceStatus))]
[JsonSerializable(typeof(AwdpTeamServiceStatus[]))]
[JsonSerializable(typeof(AwdpScoreboardItem))]
[JsonSerializable(typeof(AwdpScoreboardItem[]))]
[JsonSerializable(typeof(AwdpAttackLogItem))]
[JsonSerializable(typeof(AwdpAttackLogItem[]))]
[JsonSerializable(typeof(ArrayResponse<AwdpAttackLogItem>))]
[JsonSerializable(typeof(AwdpPatchStatusItem))]
[JsonSerializable(typeof(AwdpPatchStatusItem[]))]
[JsonSerializable(typeof(AwdpPatchSubmissionViewModel))]
[JsonSerializable(typeof(AwdpPatchSubmissionViewModel[]))]
[JsonSerializable(typeof(ArrayResponse<AwdpPatchSubmissionViewModel>))]
[JsonSerializable(typeof(AwdpServiceStatusModel))]
[JsonSerializable(typeof(AwdpServiceStatusModel[]))]
[JsonSerializable(typeof(AwdpPatchResultModel))]
[JsonSerializable(typeof(AwdpInstanceActionModel))]
[JsonSerializable(typeof(PenetrationWorkspaceModel))]
[JsonSerializable(typeof(PenetrationSubmitModel))]
[JsonSerializable(typeof(PenetrationSubmitResultModel))]
[JsonSerializable(typeof(PenetrationWorkspaceUpdateModel))]
[JsonSerializable(typeof(PenetrationScoreboardItemModel))]
[JsonSerializable(typeof(PenetrationScoreboardItemModel[]))]
[JsonSerializable(typeof(PenetrationSubmissionLogModel))]
[JsonSerializable(typeof(PenetrationSubmissionLogModel[]))]
[JsonSerializable(typeof(ArrayResponse<PenetrationSubmissionLogModel>))]
[JsonSerializable(typeof(TeamLabUdpMappingEntry))]
[JsonSerializable(typeof(TeamLabUdpMappingEntry[]))]
[JsonSerializable(typeof(GameEvent[]))]
[JsonSerializable(typeof(Submission[]))]
[JsonSerializable(typeof(CheatInfoModel[]))]
[JsonSerializable(typeof(ChallengeTrafficModel[]))]
[JsonSerializable(typeof(TeamTrafficModel[]))]
[JsonSerializable(typeof(FileRecord[]))]
[JsonSerializable(typeof(GameDetailModel))]
[JsonSerializable(typeof(ParticipationInfoModel[]))]
[JsonSerializable(typeof(ChallengeDetailModel))]
[JsonSerializable(typeof(FlagSubmitResultModel))]
[JsonSerializable(typeof(BasicWriteupInfoModel))]
[JsonSerializable(typeof(PostInfoModel[]))]
[JsonSerializable(typeof(ClientConfig))]
[JsonSerializable(typeof(ClientCaptchaInfoModel))]
[JsonSerializable(typeof(TeamInfoModel))]
[JsonSerializable(typeof(TeamInfoModel[]))]
[JsonSerializable(typeof(ApiTokenModel))]
[JsonSerializable(typeof(ApiTokenResponse))]
[JsonSerializable(typeof(ApiTokenModel[]))]
[JsonSerializable(typeof(ApiOperationModel))]
[JsonSerializable(typeof(OpenChallengeImportModel))]
[JsonSerializable(typeof(OpenChallengeBatchImportModel))]
[JsonSerializable(typeof(OpenChallengeBatchDeleteModel))]
[JsonSerializable(typeof(OpenChallengeModel))]
[JsonSerializable(typeof(OpenChallengeSummaryModel))]
[JsonSerializable(typeof(OpenChallengePageModel))]
[JsonSerializable(typeof(OpenChallengeMutationResult))]
internal sealed partial class AppJsonSerializerContext : JsonSerializerContext;

public class DateTimeOffsetJsonConverter : JsonConverter<DateTimeOffset>
{
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Number
            ? DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64())
            : reader.GetDateTimeOffset();

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value.ToUnixTimeMilliseconds());
}

public class IPAddressJsonConverter : JsonConverter<IPAddress>
{
    public override IPAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var str = reader.GetString();
        if (str == null || !IPAddress.TryParse(str, out var address))
            return IPAddress.Any;
        return address;
    }

    public override void Write(Utf8JsonWriter writer, IPAddress value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

public class OpenApiDateTimeOffsetToUIntMapper : ITypeMapper
{
    public void GenerateSchema(JsonSchema schema, TypeMapperContext context)
    {
        schema.Type = JsonObjectType.Integer;
        schema.Format = JsonFormatStrings.ULong;
    }

    public Type MappedType => typeof(DateTimeOffset);

    public bool UseReference => false;
}

public class OpenApiIPAddressToStringMapper : ITypeMapper
{
    public void GenerateSchema(JsonSchema schema, TypeMapperContext context) =>
        schema.Type = JsonObjectType.String;

    public Type MappedType => typeof(IPAddress);

    public bool UseReference => false;
}

// wait for https://github.com/RicoSuter/NJsonSchema/issues/1741
internal class GenericsSystemTextJsonReflectionService : SystemTextJsonReflectionService
{
    private static bool HasStringEnumConverter(ContextualType contextualType)
    {
        dynamic? jsonConverterAttribute = contextualType
            .GetContextOrTypeAttributes(true)?
            .FirstOrDefault(a => a.GetType().Name == "JsonConverterAttribute");

        if (jsonConverterAttribute == null ||
            !ObjectExtensions.HasProperty(jsonConverterAttribute, "ConverterType"))
            return false;

        if (jsonConverterAttribute?.ConverterType is Type converterType)
            return converterType.IsAssignableToTypeName("StringEnumConverter", TypeNameStyle.Name) ||
                   converterType.IsAssignableToTypeName("JsonStringEnumConverter`1", TypeNameStyle.Name) ||
                   converterType.IsAssignableToTypeName("System.Text.Json.Serialization.JsonStringEnumConverter",
                       TypeNameStyle.FullName);

        return false;
    }

    public override bool IsStringEnum(ContextualType contextualType, SystemTextJsonSchemaGeneratorSettings settings)
        => contextualType.TypeInfo.IsEnum && HasStringEnumConverter(contextualType);
}
