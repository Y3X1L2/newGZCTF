using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using Serilog;
using GZCTF.Infrastructure.Api;

namespace GZCTF;

public class Server
{
    internal const int MetricPort = 3001;
    internal const int ServerPort = 8080;

    internal static readonly string[] SupportedCultures =
    [
        "en-US",
        "zh-CN",
        "zh-TW",
        "ja-JP",
        "id-ID",
        "ko-KR",
        "ru-RU",
        "de-DE",
        "fr-FR",
        "es-ES",
        "vi-VN"
    ];

    private static readonly string LanguageWarning =
        $"Warning: Current language {CultureInfo.CurrentCulture.DisplayName} is machine translated and may not be accurate.\n";

    internal static IStringLocalizer<Program> StaticLocalizer { get; } =
        new CulturedLocalizer<Program>(CultureInfo.CurrentCulture);

    internal static void Banner()
    {
        const string banner =
            """
            __   __ ___ _   _ __   __ _   _      ____ _____ _____
            \ \ / /|_ _| \ | |\ \ / /| | | |    / ___|_   _|  ___|
             \ V /  | ||  \| | \ V / | | | |   | |     | | | |_
              | |   | || |\  |  | |  | |_| |   | |___  | | |  _|
              |_|  |___|_| \_|  |_|   \___/     \____| |_| |_|

                        YINYU CTF Platform
            """ + "\n";
        Console.WriteLine(banner);

        var versionStr = "";
        var version = typeof(Program).Assembly.GetName().Version;
        if (version is not null)
            versionStr = $"Version: {version.Major}.{version.Minor}.{version.Build}";

        // ReSharper disable once LocalizableElement
        Console.WriteLine($"YINYU CTF Platform {versionStr,33}\n");

        // Show warning if a language is machine translated
        string[] machineTranslated = ["de-DE", "fr-FR", "es-ES"];
        if (machineTranslated.Contains(CultureInfo.CurrentCulture.Name))
            Console.WriteLine(LanguageWarning);
    }

    internal static void ExitWithFatalMessage(string msg)
    {
        Log.Logger.Fatal("{msg}", msg);
        Thread.Sleep(30000);
        Environment.Exit(1);
    }

    internal static IActionResult InvalidModelStateHandler(ActionContext context)
    {
        var localizer =
            context.HttpContext.RequestServices.GetRequiredService<IStringLocalizer<Program>>();
        if (context.ModelState.ErrorCount <= 0)
            return RequestResponse.Result(
                localizer[nameof(Resources.Program.Model_ValidationFailed)]);

        var error = context.ModelState.Values.Where(v => v.Errors.Count > 0)
            .Select(v => v.Errors.FirstOrDefault()?.ErrorMessage).FirstOrDefault();

        var detail = error is [_, ..]
            ? error
            : localizer[nameof(Resources.Program.Model_ValidationFailed)].Value;
        if (!context.HttpContext.Request.Path.StartsWithSegments(
                "/api/open/v1", StringComparison.OrdinalIgnoreCase))
            return RequestResponse.Result(detail);

        var result = new ObjectResult(ExternalApiProblemDetails.Create(
            context.HttpContext,
            StatusCodes.Status400BadRequest,
            "validation_failed",
            "The request is invalid.",
            detail))
        {
            StatusCode = StatusCodes.Status400BadRequest
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }
}
