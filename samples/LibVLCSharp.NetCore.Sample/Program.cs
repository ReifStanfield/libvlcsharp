using System;
using LibVLCSharp;

namespace LibVLCSharp.NetCore.Sample
{
    class Program
    {
        static void Main(string[] args)
        {
            using var libVLC = new LibVLC(enableDebugLogs: true);
            using var media = new Media(new Uri("https://download.blender.org/peach/trailer/trailer_480p.mov"));
            using var mp = new MediaPlayer(libVLC, media);
            mp.Play();
            Console.ReadKey();
        }
    }
}
