"""Regenerate the embedded animation sheet and icon with Pillow."""
import base64
from io import BytesIO
from pathlib import Path
import re
from starfish import draw_starfish

from PIL import Image, ImageDraw

SIZE = 44
INK = "#26303c"
SHELL = "#566a80"
LIGHT = "#7d94a8"
MINT = "#a7e8c6"


def pose(front=(14, 43), back=(27, 43), *, lift=0, lean=0, glance=False,
         blink=False, front_knee=None, back_knee=None):
    canvas = Image.new("RGBA", (SIZE, SIZE))
    draw = ImageDraw.Draw(canvas)
    x, y = 22 + lean, 25 - lift

    def leg(hip, foot, knee, color):
        if knee is None:
            knee = ((hip[0] + foot[0]) // 2, foot[1] - 5)
        draw.line((hip, knee, (foot[0], foot[1] - 2)), fill=INK, width=5)
        draw.line((hip, knee, (foot[0], foot[1] - 2)), fill=color, width=2)
        draw.rounded_rectangle((foot[0] - 5, foot[1] - 4, foot[0] + 2, foot[1]),
                               radius=2, fill=INK)
        draw.line((foot[0] - 4, foot[1] - 2, foot[0], foot[1] - 2), fill=color)

    leg((x + 5, y + 8), back, back_knee, SHELL)
    draw.rounded_rectangle((x - 8, y - 3, x + 9, y + 11), radius=5, fill=SHELL,
                           outline=INK, width=2)
    draw.line((x + 4, y + 6, x + 7, y + 4), fill=LIGHT, width=2)
    leg((x - 4, y + 9), front, front_knee, LIGHT)
    draw.line((x + 4, y - 17, x + 5, y - 23, x + 8, y - 24), fill=INK, width=2)
    draw.ellipse((x + 7, y - 26, x + 11, y - 22), fill=MINT, outline=INK)
    draw.rounded_rectangle((x - 13, y - 20, x + 10, y - 3), radius=5,
                           fill=SHELL, outline=INK, width=2)
    draw.rounded_rectangle((x - 12, y - 16, x + 5, y - 6), radius=4, fill=INK)
    draw.line((x - 7, y - 18, x + 5, y - 18), fill=LIGHT)
    for eye in (x - 9, x - 4) if glance else (x - 8, x - 2):
        draw.rounded_rectangle((eye, y - 14, eye + 2, y - (14 if blink else 9)),
                               radius=1, fill=MINT)
    canvas.info['rig'] = dict(center=(x, y), boots=(front, back), blink=blink, glance=glance)
    return canvas


frames = [pose(), pose(blink=True)]
# Each stance foot moves backward three pixels per frame as the body runs left.
stride = [(11, 43), (14, 43), (17, 43), (20, 43),
          (27, 39), (28, 34), (20, 32), (13, 37)]
for i, foot in enumerate(stride):
    rear = stride[(i + 4) % len(stride)]
    frames.append(pose(foot, rear, lift=(0, 1, 2, 1)[i % 4], lean=-1))

frames += [
    pose((18, 41), (26, 40), lift=1),                    # carried
    pose((14, 39), (27, 41), lift=2, lean=-2),           # air
    pose((16, 41), (27, 40), lift=1, lean=1),            # air tuck
    pose((13, 43), (28, 43), lift=-3, lean=-2),          # land
    pose((13, 43), (27, 43), lift=-1),                   # recover
    pose(glance=True, lean=-2),                         # look
    pose((15, 43), (30, 43), lift=1, lean=2, glance=True), # recoil
    pose((12, 43), (28, 43), lift=-2, lean=-2),          # brake
    pose((15, 43), (26, 43), lift=-3, lean=-1),          # crouch
    pose((18, 43), (23, 43), lift=1),                   # turn
]

for front, back in [((17, 43), (26, 41)), ((19, 41), (27, 43)), ((17, 42), (27, 42))]:
    frames.append(pose(front, back, lift=4, front_knee=(17, 33), back_knee=(25, 33)))

for high in (True, False):
    frame = pose((13, 39 if high else 43), (25, 42 if high else 37), lean=-2)
    draw = ImageDraw.Draw(frame)
    for hand, joint in [((1, 25 if high else 18), (8, 23)), ((2, 16 if high else 26), (7, 27))]:
        draw.line(((16, 28), joint, hand), fill=INK, width=4)
        draw.line(((15, 27), joint, hand), fill=LIGHT, width=2)
        draw.rectangle((0, hand[1] - 1, 3, hand[1] + 2), fill=MINT)
    frames.append(frame)

for breath in (0, 1):
    asleep = pose((15, 43), (27, 43), lift=-5 - breath, blink=True)
    draw = ImageDraw.Draw(asleep)
    draw.line((29, 7, 34, 7, 29, 12, 34, 12), fill=MINT, width=1)
    asleep.info['rig']['sleep'] = True
    frames.append(asleep)
for lean in (-3, 0, 3, 0):
    frames.append(pose((13, 43), (27, 43), lift=abs(lean), lean=lean))
for blink in (False, True):
    peek = pose(blink=blink)
    peek.paste((0, 0, 0, 0), (0, 22, SIZE, SIZE))
    peek.info['rig']['peek'] = True
    frames.append(peek)
frames += [pose((12, 43), (30, 43), lift=-1, lean=2), pose(lean=-4, glance=True)]


def climb_pose(hands, boots, *, rope=False, lean=-4, lift=0, squint=False):
    canvas = pose((14, 34), (24, 35), lift=lift, lean=lean, blink=squint)
    draw = ImageDraw.Draw(canvas)
    draw.rectangle((0, 30, SIZE, SIZE), fill=(0, 0, 0, 0))
    x = 22 + lean
    for index, (color, hand, boot) in enumerate(zip((SHELL, LIGHT), hands, boots)):
        hip = (x + (2 if index == 0 else -3), 31 - lift)
        knee = (max(4, boot[0] + 7), max(21, boot[1] - 5))
        draw.line((hip, knee, boot), fill=INK, width=6)
        draw.line((hip, knee, boot), fill=color, width=3)
        draw.rounded_rectangle((boot[0], boot[1] - 2, boot[0] + 6, boot[1] + 2), radius=1,
                               fill=color, outline=INK)
        shoulder = (x + (4 if index == 0 else -1), 23 - lift)
        elbow = (max(5, hand[0] + 5), (hand[1] + shoulder[1]) // 2 + 5)
        draw.line((shoulder, elbow, hand), fill=INK, width=5)
        draw.line((shoulder, elbow, hand), fill=color, width=2)
        draw.rectangle((hand[0], hand[1] - 1, hand[0] + 3, hand[1] + 2), fill=MINT)
    if rope:
        draw.point((hands[1][0] + 1, hands[1][1]), fill="#ffe4b0")
    canvas.info['rig'].update(hands=hands, boots=boots)
    return canvas


def steps(rope=False):
    hand = 4 if rope else 0
    foot = 11 if rope else 0
    for pass_left in (False, True):
        for phase, (reach, knee, lift) in enumerate(((22, 38, 0), (13, 33, 0), (18, 38, -1), (24, 40, 0))):
            hands = [(hand + (0 if phase else 2), reach), (hand, 27 if phase < 2 else 32)]
            boots = [(foot + (5 if phase == 1 else 0), knee), (foot, 40 if phase < 2 else 42)]
            yield climb_pose(hands[::-1] if pass_left else hands, boots[::-1] if pass_left else boots,
                             rope=rope, lift=lift, squint=phase == 2)


frames.extend(steps())
frames.extend([climb_pose([(23, 23), (28, 16)], [(12, 41), (24, 41)], lean=1),
               climb_pose([(15, 22), (15, 5)], [(12, 41), (25, 41)], lean=1),
               climb_pose([(13, 22), (1, 10)], [(10, 41), (25, 41)], lean=-3),
               climb_pose([(0, 29), (0, 25)], [(8, 38), (2, 35)], lift=4, squint=True),
               pose((10, 35), (19, 41), lift=5, lean=-5),
               climb_pose([(0, 13), (0, 16)], [(12, 35), (19, 41)], lift=2)])
frames.extend(steps(rope=True))
frames.extend([
    climb_pose([(1, 24), (0, 25)], [(7, 38), (11, 41)], lean=-2, lift=-2),
    climb_pose([(0, 36), (0, 38)], [(7, 41), (3, 36)], lean=-4, lift=2, squint=True),
    climb_pose([(0, 38), (5, 36)], [(5, 34), (17, 42)], lean=-6, lift=2),
    climb_pose([(4, 40), (8, 40)], [(14, 39), (29, 42)], lean=-8, lift=0, squint=True),
    pose((11, 43), (28, 43), lift=-5, lean=-5, front_knee=(12, 34), back_knee=(28, 34)),
    pose((14, 43), (25, 43), lift=-2, lean=-2),
])

rows = (len(frames) + 4) // 5
tile = 152
sheet = Image.new("RGBA", (tile * 5, tile * rows * 2))
for i, frame in enumerate(frames):
    x, y = (i % 5) * tile, (i // 5) * tile
    sheet.paste(draw_starfish(tile, frame.info['rig']), (x, y))
    sheet.paste(frame.resize((tile, tile), Image.Resampling.NEAREST), (x, y + rows * tile))


def encoded(image, fmt, **kwargs):
    stream = BytesIO()
    image.save(stream, fmt, **kwargs)
    return base64.b64encode(stream.getvalue()).decode("ascii")


directory = Path(__file__).resolve().parent
xml = directory / "animations.xml"
content = xml.read_text(encoding="utf-8")
content = re.sub(r"<png>.*?</png>", f"<png>{encoded(sheet, 'PNG')}</png>", content)
content = re.sub(r"<tilesy>\d+</tilesy>", f"<tilesy>{rows * 2}</tilesy>", content)
content = re.sub(r"<skins>\d+</skins>", "<skins>2</skins>", content)
icon = frames[0].crop((7, 0, 36, SIZE))
icon.thumbnail((30, 32))
square = Image.new("RGBA", (32, 32))
square.paste(icon, ((32 - icon.width) // 2, 0))
content = re.sub(r"<icon>.*?</icon>", f"<icon>{encoded(square, 'ICO', sizes=[(32, 32)], bitmap_format='bmp')}</icon>", content)
xml.write_text(content, encoding="utf-8")
