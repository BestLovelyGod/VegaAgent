// HashGenerator.cs
// 文件/字符串哈希生成工具 — 支持 MD5/SHA1/SHA256/SHA512

using System.Security.Cryptography;
using System.Text;

if (args.Length == 0)
{
    Console.WriteLine("用法: HashGenerator <action> [params]");
    Console.WriteLine("  action: file | string | verify");
    Console.WriteLine();
    Console.WriteLine("示例:");
    Console.WriteLine("  HashGenerator file C:\\path\\to\\file.txt");
    Console.WriteLine("  HashGenerator string \"Hello World\"");
    Console.WriteLine("  HashGenerator verify C:\\file.txt abc123def SHA256");
    return;
}

var action = args[0].ToLowerInvariant();

try
{
    switch (action)
    {
        case "file":
            if (args.Length < 2) { Console.Error.WriteLine("需要文件路径"); return; }
            HashFile(args[1]);
            break;
        case "string":
            if (args.Length < 2) { Console.Error.WriteLine("需要字符串"); return; }
            HashString(args[1]);
            break;
        case "verify":
            if (args.Length < 4) { Console.Error.WriteLine("verify 需要: 文件路径, 期望哈希, 算法"); return; }
            VerifyHash(args[1], args[2], args[3]);
            break;
        default:
            Console.Error.WriteLine($"未知操作: {action}");
            break;
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"错误: {ex.Message}");
}

static void HashFile(string filePath)
{
    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"文件不存在: {filePath}");
        return;
    }

    var data = File.ReadAllBytes(filePath);
    var fileInfo = new FileInfo(filePath);

    Console.WriteLine($"文件: {filePath}");
    Console.WriteLine($"大小: {fileInfo.Length:N0} 字节");
    Console.WriteLine();

    foreach (var algo in new[] { "MD5", "SHA1", "SHA256", "SHA512" })
    {
        var hash = ComputeHash(data, algo);
        Console.WriteLine($"{algo,-8}: {hash}");
    }
}

static void HashString(string input)
{
    var data = Encoding.UTF8.GetBytes(input);

    Console.WriteLine($"输入: {(input.Length > 100 ? input[..100] + "..." : input)}");
    Console.WriteLine($"长度: {data.Length} 字节");
    Console.WriteLine();

    foreach (var algo in new[] { "MD5", "SHA1", "SHA256", "SHA512" })
    {
        var hash = ComputeHash(data, algo);
        Console.WriteLine($"{algo,-8}: {hash}");
    }
}

static void VerifyHash(string filePath, string expectedHash, string algorithm)
{
    if (!File.Exists(filePath))
    {
        Console.Error.WriteLine($"文件不存在: {filePath}");
        return;
    }

    var data = File.ReadAllBytes(filePath);
    var actualHash = ComputeHash(data, algorithm);
    var match = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);

    Console.WriteLine($"文件: {filePath}");
    Console.WriteLine($"算法: {algorithm}");
    Console.WriteLine($"期望: {expectedHash}");
    Console.WriteLine($"实际: {actualHash}");
    Console.WriteLine();
    Console.WriteLine(match ? "✅ 哈希匹配" : "❌ 哈希不匹配");
}

static string ComputeHash(byte[] data, string algorithm)
{
    byte[] hash = algorithm.ToUpperInvariant() switch
    {
        "MD5" => MD5.HashData(data),
        "SHA1" => SHA1.HashData(data),
        "SHA256" => SHA256.HashData(data),
        "SHA512" => SHA512.HashData(data),
        _ => throw new ArgumentException($"不支持的算法: {algorithm}")
    };

    return Convert.ToHexString(hash).ToLowerInvariant();
}
