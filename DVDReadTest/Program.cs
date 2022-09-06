
// See https://aka.ms/new-console-template for more information

using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Text;


using DVDUtilities;
using MediaInfoLib;

NEW_VMG vmg = new NEW_VMG(@"E:\VIDEO_TS\VIDEO_TS.IFO");





Environment.Exit(0);










var d = new DVDInfo("E:");

foreach (var title in d.titles)
{
    Console.WriteLine("Title # {0}", title.Id + 1);

    foreach (var audio_track in title.AudioTracks)
    {
        string s = String.Format("\tAudio track # {0}:  {1}", audio_track.Id + 1, audio_track.Language);
        s += String.Format(" ({0})", audio_track.CodingMode.ToString());
        s += String.Format(" ({0} ch)", audio_track.Channels.ToString());
        //if (audio_track.DolbySurround)
        //    s += " (Dolby Surround)";
        Console.WriteLine(s);
    }

    foreach (var sub_track in title.SubtitleTracks)
    {
        Console.WriteLine(String.Format("\tSubtitle track [{0}],  {1}", sub_track.Id, sub_track.SubtitleDescription));
    }

}

Environment.Exit(0);

/*
 * 
 * MediaInfo has 99% of the information we need to parse from the disk.  The ONLY thing
 * missing is the relationship between titles and video title sets.  That means the VIDEO_TS.IFO
 * file must be parsed manually to get the title/vts relationships from TT_SRPT.  The VTS # from the
 * IFO can be used to generate the filenames we need to parse with MediaInfo.
 * 
 */



MediaInfo mi = new MediaInfo();
Console.WriteLine(mi.Option("Info_version"));


/*
 * Process the IFO and display
 */


mi.Open(@"e:\video_ts\vts_01_0.ifo");
Console.WriteLine("\n\nData for:  VTS_01_0.IFO");
Console.WriteLine(String.Format("    Length in ms:  {0}", mi.Get(StreamKind.General, 0, "Duration")));
Console.WriteLine(String.Format("    Video Count: [{0}], Audio Count: [{1}], Subtitle Count: [{2}]",
                    mi.Get(StreamKind.General, 0, "VideoCount"),
                    mi.Get(StreamKind.General, 0, "AudioCount"),
                    mi.Get(StreamKind.General, 0, "TextCount")));
Console.WriteLine();

for (int i = 0; i < mi.Count_Get(StreamKind.Video); i++)
{
    Console.WriteLine(String.Format("    Video [{0}]: ID[0x{1}], Format[{2}:{3}], Size[{4}x{5}], [{6}], [{7}], [{8} fps]", i, 
        Int32.Parse(mi.Get(StreamKind.Video, 0, "ID")).ToString("X"),
        mi.Get(StreamKind.Video, i, "Format"),
        mi.Get(StreamKind.Video, i, "Format_Version"),
        mi.Get(StreamKind.Video, i, "Width"),
        mi.Get(StreamKind.Video, i, "Height"),
        mi.Get(StreamKind.Video, i, "DisplayAspectRatio/String"),
        mi.Get(StreamKind.Video, i, "Standard"),
        mi.Get(StreamKind.Video, i, "FrameRate")   ));
}
Console.WriteLine();

for (int i = 0; i < mi.Count_Get(StreamKind.Audio); i++)
{
    Console.Write(String.Format("    Audio [{0}]: ", i));
    Console.Write(String.Format("ID[{0}]", mi.Get(StreamKind.Audio, i, "ID")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Audio, i, "Language/String")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Audio, i, "Format")));
    Console.Write(String.Format(", [{0} ch]", mi.Get(StreamKind.Audio, i, "Channel(s)")));

    string s = mi.Get(StreamKind.Audio, i, "Language_More");
    if (s != String.Empty)
        Console.Write(String.Format(", [{0}]", s));
    Console.WriteLine();
}
Console.WriteLine();


for (int i = 0; i < mi.Count_Get(StreamKind.Text); i++)
{
    Console.Write(String.Format("    Subtitle [{0}]: ", i));
    Console.Write(String.Format("ID[{0}]", mi.Get(StreamKind.Text, i, "ID")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Text, i, "Language/String")));
    string s = mi.Get(StreamKind.Text, i, "Language_More");
    if (s != String.Empty)
        Console.Write(String.Format(", [{0}]", s));

    Console.WriteLine();
}
Console.WriteLine();
mi.Close();


/*
 * Process the VOB and display.  Some fields are only available in the VOB
 */


// Remember, the VTS_01_0.VOB is the MENU video
mi.Open(@"E:\VIDEO_TS\VTS_01_1.VOB");
Console.WriteLine("Data for:  VTS_01_1.VOB");
Console.WriteLine(String.Format("    Length in ms:  {0}", mi.Get(StreamKind.General, 0, "Duration")));
Console.WriteLine(String.Format("    Video Count: [{0}], Audio Count: [{1}], Subtitle Count: [{2}]",
                    mi.Get(StreamKind.General, 0, "VideoCount"),
                    mi.Get(StreamKind.General, 0, "AudioCount"),
                    mi.Get(StreamKind.General, 0, "TextCount")));
Console.WriteLine();


for (int i = 0; i < mi.Count_Get(StreamKind.Video); i++)
{
    Console.WriteLine(String.Format("    Video [{0}]: ID[0x{1}], Format[{2}:{3}], Size[{4}x{5}], [{6}], [{7} fps]", i,
        Int32.Parse(mi.Get(StreamKind.Video, 0, "ID")).ToString("X"),
        mi.Get(StreamKind.Video, i, "Format"),
        mi.Get(StreamKind.Video, i, "Format_Version"),
        mi.Get(StreamKind.Video, i, "Width"),
        mi.Get(StreamKind.Video, i, "Height"),
        mi.Get(StreamKind.Video, i, "DisplayAspectRatio/String"),
        mi.Get(StreamKind.Video, i, "FrameRate")));
}
Console.WriteLine();


int audio_count = mi.Count_Get(StreamKind.Audio);
for (int i = 0; i < audio_count; i++)
{
    Console.Write(String.Format("    Audio [{0}]: ", i));
    Console.Write(String.Format("ID[{0}]", mi.Get(StreamKind.Audio, i, "ID/String")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Audio, i, "Format")));
    Console.Write(String.Format(", [{0} ch]", mi.Get(StreamKind.Audio, i, "Channel(s)")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Audio, i, "ChannelLayout")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Audio, i, "BitRate/String")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Audio, i, "BitRate_Mode")));
    Console.WriteLine();
}
Console.WriteLine();


for (int i = 0; i < mi.Count_Get(StreamKind.Text); i++)
{
    Console.Write(String.Format("    Subtitle [{0}]: ", i));
    Console.Write(String.Format("ID[{0}]", mi.Get(StreamKind.Text, i, "ID/String")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Text, i, "Format")));
    Console.Write(String.Format(", [{0}]", mi.Get(StreamKind.Text, i, "MuxingMode")));

    Console.WriteLine();
}
Console.WriteLine();





mi.Close();











