from PIL import Image, ImageDraw, ImageFont
from pathlib import Path
import math

size=1024
image=Image.new('RGB',(size,size),(14,20,31))
draw=ImageDraw.Draw(image)
draw.rounded_rectangle((25,25,999,999),radius=150,fill=(19,31,43),outline=(51,77,91),width=10)
for radius in range(330,50,-10):
    color=(19,34+int((330-radius)*.08),49+int((330-radius)*.07))
    draw.ellipse((512-radius,430-radius,512+radius,430+radius),fill=color)
# A geometric shield and three attenuated spell rays, drawn independently.
shield=[(512,168),(755,264),(730,483),(644,633),(512,728),(380,633),(294,483),(269,264)]
draw.polygon(shield,fill=(24,58,67))
draw.line(shield+[shield[0]],fill=(90,226,208),width=19,joint='curve')
draw.line([(512,215),(512,675)],fill=(62,138,144),width=7)
for y,tail in [(347,430),(455,520),(559,603)]:
    draw.line([(180,y),(330,y),(400,y-43),(466,y-43)],fill=(239,181,90),width=17,joint='curve')
    draw.line([(565,y-43),(625,y-43),(690,y),(tail+365,y)],fill=(119,189,203),width=10,joint='curve')
    draw.ellipse((453,y-56,479,y-30),fill=(253,207,127))
font_path=Path(r'C:\Windows\Fonts\segoeuib.ttf')
font=ImageFont.truetype(str(font_path),94)
small=ImageFont.truetype(str(font_path),35)
draw.text((512,794),'EFFECTS',font=font,anchor='mm',fill=(232,243,240))
draw.text((512,881),'CONTROL',font=small,anchor='mm',fill=(112,205,200),stroke_width=0)
image.resize((256,256),Image.Resampling.LANCZOS).save(Path(__file__).parent.parent/'packaging'/'icon.png')
