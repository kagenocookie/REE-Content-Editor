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
border (sm39_090_05_Env)     1   3 4         9    11 12             17     expects: scale=128 offset=1     = 8192 / 64 = 128
crane (sm23_018_0100)          2 3           9 10 11                17     expects: scale=16  offset=1
cables (sm30_097_00)             3           9                      17     expects: scale=2   offset=4     = 2 / 4 = 0.5 * offset = 2

bag (sm34_011_00)                            9 10 11 12 13 14 15 16        expects: prescale=0.5   offset=2   final_scale=1
box (sm33_031_00)              2 3 4 5 6 7 8   10 11 12 13 14 15 16        expects: prescale=1     offset=1   final_scale=1
cup (sm63_080_05)              2   4 5 6 7 8         12 13 14 15 16        expects: prescale=.0625 offset=2   final_scale=.125
wrapped food (sm63_080_04)     2   4 5 6 7 8   10    12 13 14 15 16        expects: prescale=.25   offset=1   final_scale=.25
biscuits (sm63_080_03)           3 4 5 6 7 8   10    12 13 14 15 16        expects: prescale=.125  offset=2   final_scale=.25
padded_box                       3 4 5 6 7 8      11 12 13 14 15 16        expects: prescale=.5    offset=1   final_scale=.5
disks (sm76_003_02)            2 3 4 5 6 7 8      11 12 13 14 15 16        expects: prescale=.25   offset=2   final_scale=.5


box         = mult = 16384(14), div = 32768(15)  => 2 * 16384 / 32768 = 1
sm63_080_05 = mult = 256(8),   div = 8192(13)   => 2 * 256 / 8192   = 1/16 = 0.0625  + offset
sm63_080_03 = mult = 1024(10),  div = 16384(14)  => 2 * 1024 / 16384 = 1/8  = 0.125   + offset
sm63_080_04 = mult = 1024(10),  div = 8192(13)   => 2 * 1024 / 8192  = 1/4  = 0.25
sm76_003_02 = mult = 4096(12),  div = 32768(15)  => 2 * 4096 / 32768 = 1/4  = 0.25    + offset
padded_box  = mult = 4096(12),  div = 16384(14)  => 2 * 4096 / 16384 = 1/2  = 0.5
sm34_011_00 = mult = 32768(15), div = 0?? =      + offset



formula if flag 17:
baseVal = 1 << (binary number of bits 9 10 11 12);
divisor = 1 << (binary number of bits 2 3 4 5);
prescale = baseVal / divisor
offset = 1
if (prescale < divisor) offset *= 2;
if (prescale * 2 < divisor) offset *= 2;
scale = prescale * offset


formula if not 17 (likely incomplete due to lack of sample files):
baseVal = 1 << (binary number of bits 9 10 11 12);
divisor = 1 << (binary number of bits 2 3 4 5);
// edge case: if (divisor == 1) => divisor = 1<<17
prescale = baseVal / divisor
offset = ??

Some mply meshes also have a few low-vert-count chunks with seemingly extra compressed verts. The problem is that these verts don't seem to be functional whatsoever and just look like garbage. Absolutely and utterly fucked if we include the "compressed" vert chunks: Environment/sm/sm3X/sm39/sm39_090/sm39_090_0105.mesh
