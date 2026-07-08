using System.Text.Json;
using GZCTF.Services;
using Xunit;

namespace GZCTF.Test.UnitTests.Services;

public class PortalSsoServiceTests
{
    [Fact]
    public void ParseProfileResponse_AcceptsPortalEnvelope()
    {
        using var document = JsonDocument.Parse("""
        {
          "ok": true,
          "data": {
            "user": {
              "id": 12,
              "username": "portal-admin",
              "real_name": "Portal Admin",
              "role_code": "super_admin",
              "role_name": "Super Admin",
              "class_id": null,
              "class_name": null
            },
            "permissions": ["platform:ctf-competition:view"],
            "menus": [
              {
                "id": 1,
                "parent_id": null,
                "menu_name": "Dashboard",
                "menu_code": "dashboard",
                "path": "/demo/dashboard",
                "icon": "dashboard",
                "sort_order": 1
              }
            ],
            "platforms": [
              {
                "code": "ctf-competition",
                "name": "CTF Competition",
                "icon": "flag",
                "entry_url": "http://106.52.207.52:42755/api/account/portal-sso?returnUrl=/",
                "description": "Competition platform"
              }
            ]
          }
        }
        """);

        var profile = PortalSsoService.ParseProfileResponse(document.RootElement, out var error);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal(12, profile.User?.Id);
        Assert.Equal("super_admin", profile.User?.RoleCode);
        Assert.Single(profile.Permissions);
        Assert.Single(profile.Menus);
        var platform = Assert.Single(profile.Platforms);
        Assert.Equal("ctf-competition", platform.Code);
        Assert.Equal("flag", platform.Icon);
        Assert.Equal("Competition platform", platform.Description);
    }

    [Fact]
    public void ParseProfileResponse_AcceptsDirectProfileForCompatibility()
    {
        using var document = JsonDocument.Parse("""
        {
          "user": {
            "id": 3,
            "username": "teacher",
            "real_name": "Teacher",
            "role_code": "teacher",
            "role_name": "Teacher",
            "class_id": 1,
            "class_name": "Class 1"
          },
          "permissions": [],
          "platforms": []
        }
        """);

        var profile = PortalSsoService.ParseProfileResponse(document.RootElement, out var error);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal(3, profile.User?.Id);
        Assert.Equal("teacher", profile.User?.RoleCode);
    }

    [Fact]
    public void ParseProfileResponse_AcceptsNestedProfileData()
    {
        using var document = JsonDocument.Parse("""
        {
          "ok": "true",
          "data": {
            "profile": {
              "user": {
                "id": 4,
                "username": "student",
                "real_name": "Student",
                "role_code": "student",
                "role_name": "Student",
                "class_id": null,
                "class_name": null
              },
              "permissions": [],
              "platforms": []
            }
          }
        }
        """);

        var profile = PortalSsoService.ParseProfileResponse(document.RootElement, out var error);

        Assert.Null(error);
        Assert.NotNull(profile);
        Assert.Equal(4, profile.User?.Id);
    }

    [Fact]
    public void ParseProfileResponse_ReturnsEnvelopeErrorWhenNotSuccessful()
    {
        using var document = JsonDocument.Parse("""
        {
          "ok": false,
          "message": "token expired"
        }
        """);

        var profile = PortalSsoService.ParseProfileResponse(document.RootElement, out var error);

        Assert.Null(profile);
        Assert.Equal("token expired", error);
    }

    [Fact]
    public void ParseProfileResponse_ReturnsNestedEnvelopeErrorWhenSessionExpired()
    {
        using var document = JsonDocument.Parse("""
        {
          "ok": false,
          "data": {
            "error": "session expired"
          }
        }
        """);

        var profile = PortalSsoService.ParseProfileResponse(document.RootElement, out var error);

        Assert.Null(profile);
        Assert.Equal("session expired", error);
    }
}
