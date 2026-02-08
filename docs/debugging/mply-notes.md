## MPLY mesh flag debugging info
Mesh verts are scaled based on a set of flags, starting from index 14 (meaning flags 14-31). It's actually 16 and on but whatever.

The last bit is used as a mode switch that changes the formula to use exponent division math.

Pragmata st14_903_static scene
pillar (sm30_050_00 #66)       2 3           9 10 11                17     expects: scale=16  offset=1     = 128 / 8  = 16
reflector (sm35_043_00)            4         9       12             17     expects: scale=32  offset=1     = 512 / 16 = 32
create cage (sm33_047_00):                   9                      17     expects: scale=2   offset=1     = 2
canister rack (sm31_017_00_02) 2             9 10                   17     expects: scale=4   offset=1     = 8 / 2 = 4
long light (sm30_041_00):      2 3           9 10 11                17     expects: scale=16  offset=1     = 128 / 8  = 16
arm top (sm39_066_0002)      1 2 3           9 10 11                17     expects: scale=16  offset=1     = 128 / 8 = 16
arm base (sm39_066_0001)     1   3           9    11                17     expects: scale=8   offset=1     = 32 / 4 = 8
arm elbow (sm39_066_0003)    1 2 3           9    11                17     expects: scale=8   offset=2     = 32 / 8 = 4  * offset = 8
quad-fences (sm26_011_01)    1     4         9 10 11                17     expects: scale=16  offset=2     = 128 / 16 = 8  * offset = 16
panel frame (sm39_090_0105)    2 3           9 10 11                17     expects: scale=16  offset=1     = 128 / 8 = 16
floor (sm30_030_04)          1   3           9    11                17     expects: scale=8,  offset=1     = 32 / 4 = 8
exit door:                   1   3           9    11                17     expects: scale=8,  offset=1     = 32 / 4 = 8
border (sm39_090_05_Env)     1   3 4         9    11 12             17     expects: scale=128 offset=1     = 8192 / 32 = 128
crane (sm23_018_0100)          2 3           9 10 11                17     expects: scale=16  offset=1

padded box                     3 4 5 6 7 8      11 12 13 14 15 16        expects: scale=.5  offset=1
long padded box              2 3 4 5 6 7 8   10 11 12 13 14 15 16        expects: scale=1   offset=1
box                          2 3 4 5 6 7 8   10 11 12 13 14 15 16        expects: scale=1   offset=1
big cushion                                9 10 11 12 13 14 15 16        expects: scale=1   offset=2


formula if flag 17:
9 10 11 12 are binary exponent bits (1 2 4 8)       // 9 10 11 = 2^7 = 128    9 12 = 2^9 = 512   9 10 = 2^3 = 8
2 3 4 are multiplicative divisors (*2, *4, *16) (and possibly 5 = 256)    `2 3 = 2x4=8`
baseVal = 1 << exp;
prescale = baseVal / divisor
offset = (prescale < divisor) ? 2 : 1;
scale = prescale * offset


formula if not 17 (likely incomplete due to lack of sample files):
2 => *2
9 => offset=2 (and therefore also scale *= 2)
10 does nothing?

Some mply meshes also have a few low-vert-count chunks with seemingly extra compressed verts. The problem is that these verts don't seem to be functional whatsoever and just look like garbage. Absolutely and utterly fucked if we include the "compressed" vert chunks: Environment/sm/sm3X/sm39/sm39_090/sm39_090_0105.mesh
