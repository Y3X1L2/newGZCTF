using System;
using System.Linq;
using AgentSignalStage = GZCTF.Agent.Models.AgentRuntimeSignalStage;
using MainSignalStage = GZCTF.Modules.Runtime.Contracts.AgentRuntimeSignalStage;
using GZCTF.GuestControl.Contracts;
using GZCTF.Models.Data;
using GZCTF.Modules.Content.Application;
using GZCTF.Modules.Content.Domain;
using GZCTF.Modules.TeamLab.Domain.Runtime;
using Xunit;

namespace GZCTF.Test.UnitTests.Runtime;

public sealed class GuestControlContractTests
{
    private static readonly GuestAssetIdentity Identity = new(
        Guid.Parse("019f7000-0000-7000-8000-000000000001"),
        42,
        3,
        "ad-dc",
        "tl42-ad-dc",
        Guid.Parse("019f7000-0000-7000-8000-000000000002"),
        1);

    [Fact]
    public void GuestControlContract_NegotiatesOnlyOverlappingVersions()
    {
        Assert.Equal(GuestControlProtocol.SchemaVersion,
            GuestControlProtocol.Negotiate(GuestControlProtocol.MinimumCompatibleVersion,
                GuestControlProtocol.SchemaVersion));

        var error = Assert.Throws<GuestControlProtocolException>(() =>
            GuestControlProtocol.Negotiate(GuestControlProtocol.SchemaVersion + 1,
                GuestControlProtocol.SchemaVersion + 1));
        Assert.Equal("guest_protocol_incompatible", error.Code);
    }

    [Theory]
    [InlineData("generation", "guest_generation_stale")]
    [InlineData("native-id", "guest_native_vm_mismatch")]
    [InlineData("boot-epoch", "guest_boot_epoch_mismatch")]
    public void GuestControlContract_RejectsIdentityFenceViolations(string mutation, string expectedCode)
    {
        var actual = mutation switch
        {
            "generation" => Identity with { Generation = Identity.Generation - 1 },
            "native-id" => Identity with { NativeVmId = Guid.NewGuid() },
            "boot-epoch" => Identity with { BootEpoch = Identity.BootEpoch + 1 },
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var signal = Event(actual, 2, "sha256:event-2");

        var error = Assert.Throws<GuestControlProtocolException>(() =>
            GuestControlContractValidator.ValidateEvent(Identity, 1, "sha256:event-1", signal));

        Assert.Equal(expectedCode, error.Code);
    }

    [Fact]
    public void GuestControlContract_DeduplicatesIdenticalSequenceAndRejectsConflictingPayload()
    {
        var duplicate = GuestControlContractValidator.ValidateEvent(
            Identity, 7, "sha256:same", Event(Identity, 7, "sha256:same"));

        Assert.Equal(GuestEventDisposition.Duplicate, duplicate);
        var error = Assert.Throws<GuestControlProtocolException>(() =>
            GuestControlContractValidator.ValidateEvent(
                Identity, 7, "sha256:first", Event(Identity, 7, "sha256:conflict")));
        Assert.Equal("guest_event_sequence_conflict", error.Code);
    }

    [Fact]
    public void GuestControlContract_EnrollmentCarriesCsrAndNeverPrivateKey()
    {
        var request = new GuestEnrollmentRequest(
            GuestControlProtocol.SchemaVersion,
            Identity,
            "-----BEGIN CERTIFICATE REQUEST-----\nfixture\n-----END CERTIFICATE REQUEST-----",
            GuestControlProtocol.CsrAlgorithm,
            "sha256:intent",
            DateTimeOffset.UtcNow);

        GuestControlContractValidator.ValidateEnrollment(request, Identity);
        var error = Assert.Throws<GuestControlProtocolException>(() =>
            GuestControlContractValidator.ValidateEnrollment(
                request with
                {
                    CertificateSigningRequestPem =
                        "-----BEGIN PRIVATE KEY-----\nfixture\n-----END PRIVATE KEY-----"
                }, Identity));
        Assert.Equal("guest_enrollment_request_invalid", error.Code);
        Assert.DoesNotContain("PrivateKey", typeof(GuestEnrollmentResponse).GetProperties()
            .Select(item => item.Name));
    }

    [Fact]
    public void RuntimeSignalContract_PreservesHistoricalValuesAndMapsGuestStagesExplicitly()
    {
        Assert.Equal(3, (byte)MainSignalStage.GuestReady);
        Assert.Equal(8, (byte)MainSignalStage.HealthReady);
        Assert.Equal(byte.MaxValue, (byte)MainSignalStage.Failed);
        Assert.Equal(3, (byte)AgentSignalStage.GuestReady);
        Assert.Equal(8, (byte)AgentSignalStage.HealthReady);

        Assert.Equal(MainSignalStage.GuestEnrolled,
            GZCTF.Modules.Runtime.Contracts.GuestRuntimeSignalMapper.ToRuntimeSignalStage(
                GuestLifecycleStage.GuestEnrolled));
        Assert.Equal(AgentSignalStage.ObservationReady,
            GZCTF.Agent.Models.GuestRuntimeSignalMapper.ToRuntimeSignalStage(
                GuestLifecycleStage.ObservationReady));
    }

    [Fact]
    public void BootstrapProfileCompatibility_RequiresCurrentPreparedContractFacts()
    {
        var template = new ImageTemplate
        {
            Id = 9,
            ImageHash = new string('a', 64),
            VmArtifactStatus = VmArtifactStatus.Ready,
            VmRuntimeMode = VmRuntimeMode.Managed,
            PreparedArtifactId = 17
        };
        var certification = new ImageTemplateCapabilityCertification
        {
            ImageTemplateId = template.Id,
            ImageHash = template.ImageHash,
            Status = ImageTemplateCertificationStatus.Certified,
            ProbeKind = "controlled-probe",
            CapabilitiesJson = "[\"guest.supervisor.v1\",\"image.vm.prepared.v1\"]",
            PreparationContractVersion = GuestControlProtocol.PreparationContractVersion,
            GuestProtocolVersion = GuestControlProtocol.SchemaVersion
        };

        Assert.True(BootstrapProfileCompatibilityService.IsCurrentManagedCertification(
            certification, template));
        var externalCertification = new ImageTemplateCapabilityCertification
        {
            ImageTemplateId = certification.ImageTemplateId,
            ImageHash = certification.ImageHash,
            Status = certification.Status,
            ProbeKind = "external-evidence",
            CapabilitiesJson = certification.CapabilitiesJson,
            PreparationContractVersion = certification.PreparationContractVersion,
            GuestProtocolVersion = certification.GuestProtocolVersion
        };
        Assert.False(BootstrapProfileCompatibilityService.IsCurrentManagedCertification(
            externalCertification, template));
        var legacyCertification = new ImageTemplateCapabilityCertification
        {
            ImageTemplateId = certification.ImageTemplateId,
            ImageHash = certification.ImageHash,
            Status = certification.Status,
            CapabilitiesJson = certification.CapabilitiesJson,
            PreparationContractVersion = null,
            GuestProtocolVersion = null
        };
        Assert.False(BootstrapProfileCompatibilityService.IsCurrentManagedCertification(
            legacyCertification, template));
    }

    [Fact]
    public void BootstrapExecutionContract_ProhibitsAttemptMutation()
    {
        var property = typeof(TeamLabBootstrapExecution).GetProperty(nameof(TeamLabBootstrapExecution.Attempt));

        Assert.NotNull(property);
        Assert.True(property!.SetMethod?.IsPrivate);
        Assert.Equal(1, new TeamLabBootstrapExecution().Attempt);
    }

    private static GuestLifecycleEvent Event(
        GuestAssetIdentity identity,
        long sequence,
        string digest) => new(
        GuestControlProtocol.SchemaVersion,
        identity,
        sequence,
        GuestLifecycleStage.GuestEnrolled,
        GuestLifecycleOutcome.Ready,
        DateTimeOffset.UtcNow,
        digest);
}
