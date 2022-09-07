# ListDVD

A program to read title and track information from a DVD. If there are mistakes or you have updated information, please open an
issue and I'll update as I can.

## **DVD Layout:**

You can find the structure of the data tables found on the DVD at [the SourceForge DVD info site](http://dvd.sourceforge.net/dvdinfo/)

This is definitely a work in progress. I plan to document at least the basics finding the titles and tracking down the details of the
audio and subtitle tracks.  Many of the details of audio and subtitle tracks are only found in the VOB files. Since reading those is a pain
and this code is already complex enough, this code uses the MediaInfo DLL and it's C# linkage. This can be found at [the MeidaInfo site](https://mediaarea.net/en/MediaInfo/Support/SDK).

More information is also available at [this Wikipedia entry](https://en.wikipedia.org/wiki/DVD-Video).

## **The Acronyms and their Meanings**

Many of these may be used as prefixes in the relevant tables (e.g. VTS_TT_SRPT is the TT_SRPT found in the Video Manager).

* FP: First Play program chain found in the VMG file
* VMG: Video Manager. This is the VIDEO_TS.IFO file and it contains the information the is applicable to the entire disk, including the main menu and it's related tracks (audio, video, subtitle).
* VMGM: Video Manager Menu
* VTS: Video Title Set. There can be a variable number of VTS, and each VTS can have multiple titles.
* VTSM: Video Title Set Menu
* IFO: I've not yet found an official meaning for this acronym, but I think of it as an InFOrmation file.
* MAT: This denotes the header found in one of the IFO files. For the video manager, it's called VMG_MAT. For other IFO files it's called VTS_MAT. Other sources may not give any name for this, so beware of this when viewing the above links.
* TT_SRPT: Table of titles.  This gives the title numbers and the VTS number they are found in.
* PGC: Program Chain. The sequence of data for a particular title.
* PGCI: Program chain information. This is a table that contains a count and then the entries for the program chains. Usually referenced at VTS_PGCI.
* VMGM_VOBS: Video manager menu VOB. This is used to reference video/audio/subtitle tracks for the main menu.
* PGC_AST_CTL: Audio stream control information for a program chain
* PGC_SPST_CTL: Subpicture stream control information for a PGC.  Subtitles are called subpictures in the docs.


## **Details Not Yet Mentioned**

I'm fairly certain that a conversation along the following lines happened:
```
Programmer: Hey! We have this file format ready. It's simple, flexible, and easy to use. When can you take a look at it?
Manager: Simple? Easy to use? We can't have that. Go back and make it insanely complex and then add in some quirks to 
         make it worse.
```

The layout of these files is complex and as you might guess, there are a ton of quirks about them. Once the CSS decoding was
cracked manufacturers started putting random bad information in the tables.  As a result there are some **weird** disks out
there. You can check out the source code for [HandBrake](https://www.handbrake.fr) for the details. The code is really complex,
but it does show a lot of the weirdness that various disks have.

This program is only interested in getting the title/track information so many of the tables that aren't used won't be
documented here.

**General Information:**

1.  Each VTS file can only have one video stream.
2.  Each VTS can have up to 8 audio streams.
3.  Each VTS can have up to 32 subpicture streams.
4.  The subtitle control array (PGC_SPST_CTL) has an unusual behavior. There are four entries for subtitle streams of various types in each array entry. Each stream number can only be used ONCE. If there is an entry with all four streams set to zero then there is a single subtitle that is stream #0.
5.  Titles do not have to have audio or subpicture streams.
6.  VOB files are a subset of the MPEG-2 standard. VOB files do not allow all of the features of an mpeg-2 file.


## **Rough Unrefined Layout**
```
VIDEO_TS.IFO:  Video Manager IFO file
	VMG_MAT:  Header found in the file
		  -->	TT_SRPT:  Table of titles, and the title set (VTS) they are in
				--> VTSN:  Video title set number (VTS_nn_0.IFO)
				--> VTS_TTN:  Title number inside the VTS_nn_0.IFO


VTS_nn_0.IFO:  Video Title Set nn IFO file
	VTS_MAT:  Header in the file
		-->  VTS_PTT_SRPT:  Pointer table for each title, indexed by VTS_TTN
			--> # titles
			--> VTS_PTT:  Entries that give the PGCN (program chain #) and PGN (program #)

	Video Attributes of VTS_VOBS:  Video stuff for the video in this titleset
	Audio Attributes of VTS_VOBS:  Audio attribs for each of the 8 possible audio tracks
	Subtitle Attributes of VTS_VOBS:  Subtitle attribs for each of the 32 possible subtitle tracks

	VTS_PGCI:  Title Program Chain Table header
		--> # PGCs (program chains)
		--> Array of PGCs (program chains)
			--> PGC Category
			--> offset to VTS_PGC relative to this table (program chain for this title)
				--> VTS_PGC (program chain for this title in the title set)
					--> playback time for this title
					--> Audio stream control array (which audio streams for which title)
					--> Subtitle stream control array


```
Looking in the video attributes of the titleset (found in VTS_nn_0.IFO) there are bit fields indicating
which operations are prohibited. These are used to either enable/disable subtitle tracks found in the VTS.
