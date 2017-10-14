# CLAsmTool

Here is a simple 6809 assembler I wrote to compile a Robotron 2084 disassembly I am working on in IDAPro. IDAPro outputs an asm format unusable in any assembler I am aware of, so I needed one that can handle the output format.

Currently I tested this against all the known ROM sets for Robotron 2084 I am aware of: Yellow, Blue, Fix 1987, Wave 201, and TieDie. It compiles my disassembly perfectly.

I am doing this because I want to mod my physical Robotron machine, and to do so I needed details about the code so I could modify at will, reburn ROMS, and then play :)

The assembler is written in C#. Usage is

 `clasmtool filename [options]` where filename is the file to assemble, and options are
```      
   -o outname     : assembles a total rom image to outname
   -r rompath     : where original roms are located for comparison
   -s split path  : where to put split output roms
   -c             : display rom checksums, needed in Robotron 2084 code at end
   -t num         : testing 1-4, various helps in getting code clean
   -i             : run in interactive loop (q to quit, ? help)
```


## Rom sets

Here are some details on the ROM sets I tested against. ROM images can be pulled from your robotron machine.



Yellow/Orange ROM         
|---------------|---------|----------|-----|------

|robotron.sb1      4096      f6d60e26c209c1df2cc01ac07ad5559daa1b7118      0000      maincpu      66c7d3ef  
|robotron.sb2      4096      4d6e82bc29f49100f7751ccfc6a9ff35695b84b3      1000      maincpu      5bc6c614  
|robotron.yo3      4096      5a912d485e686de5e3175d3fc0e5daad36f4b836      2000      maincpu      67a369bc  
|robotron.yo4      4096      02013e00513dd74e878a01791cbcca92712e2c80      3000      maincpu      b0de677a  
|robotron.yo5      4096      8b4ed881f64e3ce73ac1a9ae2c184721c1ab37cc      4000      maincpu      24726007  
|robotron.yo6      4096      41c4d9ece2ae8a103b7151fc4ff576796303318d      5000      maincpu      028181a6  
|robotron.yo7      4096      46fe1b1162d6054eb502852d065fc2e8c694b09d      6000      maincpu      4dfcceae  
|robotron.sb8      4096      7ae38a609ed9a6f62ca003cab719740ed7651b7c      7000      maincpu      3a96e88c  
|robotron.sb9      4096      fd9d75b866f0ebbb723f84889337e6814496a103      8000      maincpu      b124367b  
|robotron.yoa      4096      d5ae801e60ed829e7ef5c54a18aefca54eae827f      d000      maincpu      4a9d5f52  
|robotron.yob      4096      f3405be9ad2287f3921e7dbd9c5313c91fa7f8d6      e000      maincpu      2afc5e7f  
|robotron.yoc      4096      81b3b2a72a3c871e8d7b9348056622c90a20d876      f000      maincpu      45da9202  
|
|robotron.snd      4096      15afefef11bfc3ab78f61ab046701db78d160ec3      f000      soundcpu     c56c1d28  
|decoder.4          512      9988723269367fb44ef83f627186a1c88cf7877e         0      proms        e6631c23  
|decoder.6          512      30002643d08ed983a6701a7c4b5ee74a2f4a1adb       200      proms        83faf25e  
|
Name              Size      SHA1                                          Offset    Region       CRC
Blue ROM
robotron.sb1      4096      f6d60e26c209c1df2cc01ac07ad5559daa1b7118      0000      maincpu      66c7d3ef  
robotron.sb2      4096      4d6e82bc29f49100f7751ccfc6a9ff35695b84b3      1000      maincpu      5bc6c614  
robotron.sb3      4096      06a8c8dd0b4726eb7f0bb0e89c8533931d75fc1c      2000      maincpu      e99a82be  
robotron.sb4      4096      aaf89c19fd8f4e8750717169eb1af476aef38a5e      3000      maincpu      afb1c561  
robotron.sb5      4096      79b4680ce19bd28882ae823f0e7b293af17cbb91      4000      maincpu      62691e77  
robotron.sb6      4096      f76ec5432a7939b33a27be1c6855e2dbe6d9fdc8      5000      maincpu      bd2c853d  
robotron.sb7      4096      06eae5138254723819a5e93cfd9e9f3285fcddf5      6000      maincpu      49ac400c  
robotron.sb8      4096      7ae38a609ed9a6f62ca003cab719740ed7651b7c      7000      maincpu      3a96e88c  
robotron.sb9      4096      fd9d75b866f0ebbb723f84889337e6814496a103      8000      maincpu      b124367b  
robotron.sba      4096      d426a50e75dabe936de643c83a548da5e399331c      d000      maincpu      13797024  
robotron.sbb      4096      f8c6cbe3688f256f41a121255fc08f575f6a4b4f      e000      maincpu      7e3c1b87  
robotron.sbc      4096      fad7cea868ebf17347c4bc5193d647bbd8f9517b      f000      maincpu      645d543e  

robotron.snd      4096      15afefef11bfc3ab78f61ab046701db78d160ec3      f000      soundcpu     c56c1d28  
decoder.4          512      9988723269367fb44ef83f627186a1c88cf7877e         0      proms        e6631c23  
decoder.6          512      30002643d08ed983a6701a7c4b5ee74a2f4a1adb       200      proms        83faf25e  


(1987 'shot-in-the-corner' bugfix)                  
robotron.sb1      4096      f6d60e26c209c1df2cc01ac07ad5559daa1b7118      0000      maincpu      66c7d3ef  
robotron.sb2      4096      4d6e82bc29f49100f7751ccfc6a9ff35695b84b3      1000      maincpu      5bc6c614  
robotron.sb3      4096      06a8c8dd0b4726eb7f0bb0e89c8533931d75fc1c      2000      maincpu      e99a82be  
robotron.sb4      4096      aaf89c19fd8f4e8750717169eb1af476aef38a5e      3000      maincpu      afb1c561  
fixrobo.sb5        4096      1732d16cd88e0662f1cffce1aeda5c8aa8c31338      4000      maincpu      827cb5c9  
robotron.sb6      4096      f76ec5432a7939b33a27be1c6855e2dbe6d9fdc8      5000      maincpu      bd2c853d  
robotron.sb7      4096      06eae5138254723819a5e93cfd9e9f3285fcddf5      6000      maincpu      49ac400c  
robotron.sb8      4096      7ae38a609ed9a6f62ca003cab719740ed7651b7c      7000      maincpu      3a96e88c  
robotron.sb9      4096      fd9d75b866f0ebbb723f84889337e6814496a103      8000      maincpu      b124367b  
robotron.sba      4096      d426a50e75dabe936de643c83a548da5e399331c      d000      maincpu      13797024  
fixrobo.sbb        4096      4a62fcd2f91dfb609c3d2c300bd9e6cb60edf52e      e000      maincpu      e83a2eda  
robotron.sbc      4096      fad7cea868ebf17347c4bc5193d647bbd8f9517b      f000      maincpu      645d543e  

robotron.snd      4096      15afefef11bfc3ab78f61ab046701db78d160ec3      f000      soundcpu     c56c1d28  
decoder.4          512      9988723269367fb44ef83f627186a1c88cf7877e         0      proms        6631c23  
decoder.6          512      30002643d08ed983a6701a7c4b5ee74a2f4a1adb       200      proms        83faf25e  

  
(2012 'wave 201 start' hack)
Name  Size  CRC  SHA1  Region  Offset
robotron.sb1      4096      f6d60e26c209c1df2cc01ac07ad5559daa1b7118      0000      maincpu      66c7d3ef  
robotron.sb2      4096      4d6e82bc29f49100f7751ccfc6a9ff35695b84b3      1000      maincpu      5bc6c614  
wave201.sb3        4096      b6c4280415515de6f56b358206dc3bd93a12bfce      2000      maincpu      85eb583e  
robotron.sb4      4096      aaf89c19fd8f4e8750717169eb1af476aef38a5e      3000      maincpu      afb1c561  
fixrobo.sb5       4096      1732d16cd88e0662f1cffce1aeda5c8aa8c31338      4000      maincpu      827cb5c9  
robotron.sb6      4096      f76ec5432a7939b33a27be1c6855e2dbe6d9fdc8      5000      maincpu      bd2c853d  
robotron.sb7      4096      06eae5138254723819a5e93cfd9e9f3285fcddf5      6000      maincpu      49ac400c  
robotron.sb8      4096      7ae38a609ed9a6f62ca003cab719740ed7651b7c      7000      maincpu      3a96e88c  
robotron.sb9      4096      fd9d75b866f0ebbb723f84889337e6814496a103      8000      maincpu      b124367b  
robotron.sba      4096      d426a50e75dabe936de643c83a548da5e399331c      d000      maincpu      13797024  
fixrobo.sbb        4096      4a62fcd2f91dfb609c3d2c300bd9e6cb60edf52e      e000      maincpu      e83a2eda  
robotron.sbc    4096      fad7cea868ebf17347c4bc5193d647bbd8f9517b      f000      maincpu      645d543e  
                                                                                                 
robotron.snd    4096      15afefef11bfc3ab78f61ab046701db78d160ec3      f000    soundcpu     c56c1d28  
decoder.4         512      9988723269367fb44ef83f627186a1c88cf7877e         0      proms       e6631c23  
decoder.6         512      30002643d08ed983a6701a7c4b5ee74a2f4a1adb      200       proms       83faf25e  
  
(2015 'tie-die V2' hack)          
robotron.sb1      4096      f6d60e26c209c1df2cc01ac07ad5559daa1b7118      0000      maincpu      66c7d3ef
robotron.sb2      4096      4d6e82bc29f49100f7751ccfc6a9ff35695b84b3      1000      maincpu      5bc6c614
robotron.sb3      4096      06a8c8dd0b4726eb7f0bb0e89c8533931d75fc1c      2000      maincpu      e99a82be
tiedie.sb4        4096      0ce29f4bf6bdee677c8e80c2d5e66fc556ba349f      3000      maincpu      e8238019
fixrobo.sb5       4096      1732d16cd88e0662f1cffce1aeda5c8aa8c31338      4000      maincpu      827cb5c9
robotron.sb6      4096      f76ec5432a7939b33a27be1c6855e2dbe6d9fdc8      5000      maincpu      bd2c853d
tiedie.sb7        4096      3c670a1f8df35d18451c82f220a02448bf5ef5ac      6000      maincpu      3ecf4620
tiedie.sb8        4096      85dd58d14d527ca75d6c546d6271bf8ee5a82c8c      7000      maincpu      752d7a46
robotron.sb9      4096      fd9d75b866f0ebbb723f84889337e6814496a103      8000      maincpu      b124367b
tiedie.sba        4096      80f51d8e7ec62518afad7e56a47e0756f83f813c      d000      maincpu      952bea55
tiedie.sbb        4096      0d727458454826fd8222e4022b755d686ccb065f      e000      maincpu      4c05fd3c
robotron.sbc      4096      fad7cea868ebf17347c4bc5193d647bbd8f9517b      f000      maincpu      645d543e

robotron.snd      4096      15afefef11bfc3ab78f61ab046701db78d160ec3   f000       soundcpu     c56c1d28
decoder.4          512      9988723269367fb44ef83f627186a1c88cf7877e      0       proms       e6631c23
decoder.6          512      30002643d08ed983a6701a7c4b5ee74a2f4a1adb    200       proms       83faf25e



Happy hacking!