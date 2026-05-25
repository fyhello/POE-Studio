using PoeStudio.Mcp;
using System.Text;

var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var input = new StreamReader(Console.OpenStandardInput(), utf8, detectEncodingFromByteOrderMarks: false);
await using var outputStream = Console.OpenStandardOutput();
await using var errorStream = Console.OpenStandardError();
await using var outputWriter = new StreamWriter(outputStream, utf8) { AutoFlush = true };
await using var errorWriter = new StreamWriter(errorStream, utf8) { AutoFlush = true };

await McpProtocol.RunAsync(input, outputWriter, errorWriter, CancellationToken.None);
