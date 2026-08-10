using System.Text.Json;
using GZCTF.GuestControl.Contracts;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Domain;

namespace GZCTF.Modules.Content.Application;

public static class ManagedVmCertificationPolicy
{
    public static bool IsCurrent(
        ImageTemplateCapabilityCertification certification,
        ImageTemplate template)
    {
        if (template.VmRuntimeMode != VmRuntimeMode.Managed ||
            template.VmArtifactStatus != VmArtifactStatus.Ready ||
            template.PreparedArtifactId is null ||
            certification.ImageTemplateId != template.Id ||
            !string.Equals(certification.ImageHash, template.ImageHash, StringComparison.Ordinal) ||
            certification.Status != ImageTemplateCertificationStatus.Certified ||
            !string.Equals(certification.ProbeKind, "controlled-probe", StringComparison.Ordinal) ||
            certification.PreparationContractVersion != GuestControlProtocol.PreparationContractVersion ||
            certification.GuestProtocolVersion != GuestControlProtocol.SchemaVersion)
            return false;

        var capabilities = (JsonSerializer.Deserialize<string[]>(certification.CapabilitiesJson) ?? [])
            .ToHashSet(StringComparer.Ordinal);
        return capabilities.Contains(ImageTemplateCapabilityIds.GuestSupervisor) &&
               capabilities.Contains(ImageTemplateCapabilityIds.VmPreparedImage);
    }
}
