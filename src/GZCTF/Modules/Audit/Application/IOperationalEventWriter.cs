using GZCTF.Modules.Audit.Contracts;
using GZCTF.Modules.Audit.Domain;

namespace GZCTF.Modules.Audit.Application;

public interface IOperationalEventWriter
{
    OperationalEvent Append(OperationalEventDraft draft);
    Task<OperationalEvent> AppendAndSaveAsync(OperationalEventDraft draft, CancellationToken token);
}
