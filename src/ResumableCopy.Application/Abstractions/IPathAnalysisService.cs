using ResumableCopy.Application.Models;

namespace ResumableCopy.Application.Abstractions;

public interface IPathAnalysisService
{
    Task<PathAnalysis> AnalyzeSourceAsync(string path, CancellationToken cancellationToken = default);

    Task<PathAnalysis> AnalyzeDestinationAsync(string path, CancellationToken cancellationToken = default);
}
