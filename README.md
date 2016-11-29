# Austin's Migration from DropBox to OneDrive

This is my quick and dirty migration script to move my smart phone photos from DropBox to OneDrive.
It was a quick throw-away program, but there are a couple interesting things in here:

* A histrogram implementation to compare the similarty of images. The DropBox and OneDrive
  apps on iOS upload the same file somewhat differently, so this is need to compare files.
* Some SIMD code for comparing the histograms. While comparing the histograms was not the bottle neck
  (creating them was), it was still fun to lean more about this new feature of .NET.
