using ResumableCopy.Core.Abstractions;

using ResumableCopy.Core.Domain;

using ResumableCopy.Core.Security;



namespace ResumableCopy.Core.IO;



public sealed class SourceIdentityProvider : ISourceIdentityProvider

{

    private readonly IFileSystemService _fileSystemService;

    private readonly IFileIdentityProvider _fileIdentityProvider;



    public SourceIdentityProvider(

        IFileSystemService fileSystemService,

        IFileIdentityProvider? fileIdentityProvider = null)

    {

        _fileSystemService = fileSystemService ?? throw new ArgumentNullException(nameof(fileSystemService));

        _fileIdentityProvider = fileIdentityProvider ?? PlatformSecurityServices.CreateFileIdentityProvider();

    }



    public SourceIdentity Capture(string path)

    {

        ArgumentException.ThrowIfNullOrWhiteSpace(path);



        var metadata = _fileSystemService.GetMetadata(path);

        var (volumeSerial, fileId) = _fileIdentityProvider.TryGetIdentity(path);

        return new SourceIdentity(

            metadata.Length,

            metadata.LastWriteTimeUtc,

            metadata.CreationTimeUtc,

            volumeSerial,

            fileId);

    }

}

