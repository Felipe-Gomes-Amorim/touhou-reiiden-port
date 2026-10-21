namespace Th01.Assets;

/// <summary>Read-only access to the user's original game files.</summary>
public interface IAssetSource
{
    bool Exists(string name);

    /// <summary>Size in bytes, or null if the file is missing.</summary>
    long? GetSize(string name);

    /// <summary>Opens a file for reading. Throws FileNotFoundException if missing.</summary>
    Stream Open(string name);
}
