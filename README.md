# ListDVD

You can find the structure of the data tables found on the DVD at [the SourceForge DVD info site](http://dvd.sourceforge.net/dvdinfo/)

This is definitely a work in progress.  I plan to document at least the basics finding the titles and tracking down the details of the
audio and subtitle tracks.   Many of the details of audio and subtitle tracks are only found in the VOB files.  Since reading those is a pain
and this code is already complex enough, this code uses the MediaInfo DLL and it's C# linkage.  This can be found at [the MeidaInfo site](https://mediaarea.net/en/MediaInfo/Support/SDK).



DVD Layout:

VIDEO_TS.IFO:  Video Manager IFO file
	VMG_MAT:	Header found in the file
		-->	TT_SRPT:  Table of titles, and the title set (VTS) they are in
				--> VTSN:  Video title set number (VTS_nn_0.IFO)
				--> VTS_TTN:  Title number inside the VTS_nn_0.IFO


VTS_nn_0.IFO:  Video Title Set nn IFO file
	VTS_MAT:	Header in the file
		-->	VTS_PTT_SRPT:  Pointer table for each title, indexed by VTS_TTN
				--> # titles
				--> VTS_PTT:  Entries that give the PGCN (program chain #) and PGN (program #)
	Video Attributes of VTS_VOBS:  Video stuff for the video in this titleset
	Audio Attributes of VTS_VOBS:  Audio attribs for each of the 8 possible audio tracks
	Subtitle Attributes of VTS_VOBS:  Subtitle attribs for each of the 32 possible subtitle tracks

	VTS_PGCI:	Title Program Chain Table header
		-->	# PGCs (program chains)
			--> Array of PGCs (program chains)
				--> PGC Category
				--> offset to VTS_PGC relative to this table (program chain for this title)
					--> VTS_PGC (program chain for this title in the title set)
						--> playback time for this title
						--> Audio stream control array (which audio streams for which title)
						--> Subtitle stream control array


The subtitle control array (PGC_SPST_CTL) has a quirk.  There are four entries for subtitle stream in
each array entry.  Each stream number can only be used ONCE.  If there is an entry with all four 
streams set to zero then there is a single subtitle that is stream #0.

Looking in the video attributes of the titleset (found in VTS_nn_0.IFO) there are bit fields indicating
which operations are prohibited.  These are used to either enable/disable subtitle tracks found in the
.
