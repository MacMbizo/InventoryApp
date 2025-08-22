namespace QAAgentRunner.Services;

public class FileSystemService
{
    public void EnsureFileExists(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"File not found: {path}");
    }

    public string ReadAllText(string path) => File.ReadAllText(path);

    public void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, contents);
    }
}