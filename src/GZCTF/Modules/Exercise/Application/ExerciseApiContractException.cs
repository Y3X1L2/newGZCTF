using GZCTF.Modules.Audit.Application;

namespace GZCTF.Modules.Exercise.Application;

public sealed class ExerciseApiContractException(string code, string message, int statusCode)
    : ApiContractException(code, message, statusCode);