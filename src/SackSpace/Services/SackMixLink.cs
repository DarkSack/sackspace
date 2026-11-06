using System.IO;
using System.IO.Pipes;
using System.Text;

namespace SackSpace.Services;

/// <summary>
/// Tells SackMix which workspace is up ("perfil Streaming") so it can put on the mix saved
/// for it. A local pipe of this Windows user; when SackMix is not running nothing happens.
/// </summary>
public static class SackMixLink
{
    private static string PipeName => "SackMix.Control." + Environment.UserName; // same as SackMix.Core.Profiles.PipeName

    public static void Profile(string name) => Task.Run(async () =>
    {
        try
        {
            await using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(300);
            var line = Encoding.UTF8.GetBytes("perfil " + name.ReplaceLineEndings(" ") + "\n");
            await pipe.WriteAsync(line);
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { } // SackMix closed or busy
    });
}
