using GZCTF.Modules.Audit.Application;

namespace GZCTF.Modules.TeamLab.Application;

public sealed class TeamLabApiContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);
