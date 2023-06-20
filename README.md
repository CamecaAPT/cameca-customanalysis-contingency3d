# 3D Contingency Table Extension
This particular branch is to find the small discrepencies between the Miller implementation and this extension implementation.
It removes two ions and adds two ions on the range "boundary," in order to match how Miller's script handles the ranging.
There is also a difference that affects 59 ions due to minute differences between C and C# when casting a float to an int.
This is "solved" with two Lists of indices to decrement the calculated ion grid position in order to match Miller's script. 

In order to properly run this branch, use R5107_92462 with block size = 199 ions and bin size = 25 ions, decomposition turned on. 