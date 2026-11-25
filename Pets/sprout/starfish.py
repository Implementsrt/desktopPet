"""Draw the original starfish skin at native resolution from the animation rig."""
from math import hypot, sin, pi

from PIL import Image, ImageDraw, ImageFilter


def draw_starfish(size, rig):
    scale = size / 44
    mask = Image.new('L', (size, size))
    shape = ImageDraw.Draw(mask)
    center_x, center_y = rig['center']
    ink = '#823c5a'

    def coords(points):
        return [(round(x * scale), round(y * scale)) for x, y in points]

    def oval(draw, x, y, rx, ry, color):
        draw.ellipse((round((x-rx)*scale), round((y-ry)*scale), round((x+rx)*scale),
                      round((y+ry)*scale)), fill=color)

    def limb(start, end, base, tip, curve=0):
        dx, dy = end[0]-start[0], end[1]-start[1]
        length = hypot(dx, dy) or 1
        normal_x, normal_y = -dy / length, dx / length
        sides = [[], []]
        for i in range(13):
            t = i / 12
            x = start[0] + dx*t + normal_x * curve * sin(pi*t)
            y = start[1] + dy*t + normal_y * curve * sin(pi*t)
            radius = tip + (base-tip) * (1-t)**1.35
            for side, direction in zip(sides, (1, -1)):
                side.append((x+direction*normal_x*radius, y+direction*normal_y*radius))
        shape.polygon(coords(sides[0] + sides[1][::-1]), fill=255)
        oval(shape, *end, tip, tip, 255)

    x, y = center_x, center_y
    oval(shape, x, y, 10, 10.7, 255)
    limb((x, y-2), (x+1.8, max(2.4, y-22)), 9.2, 1.4, -.8)
    hands = rig.get('hands', ((x-13, y-1), (x+13, y+3)))
    for start, tip in zip(((x-5, y), (x+5, y)), hands):
        limb(start, (max(1.4, min(42, tip[0]+.7)), min(41, tip[1])), 5.8, 1.5, 2)
    for start, tip in zip(((x-4, y+7), (x+4, y+7)), rig['boots']):
        limb(start, (max(2, min(41.5, tip[0]+1)), min(42.4, tip[1]-1)), 5.6, 1.35, -.7)

    border = 2 * max(1, round(scale * .65)) + 1
    canvas = Image.new('RGBA', (size, size), ink)
    canvas.putalpha(mask.filter(ImageFilter.MaxFilter(border)))
    shades = Image.new('RGBA', (size, size))
    draw = ImageDraw.Draw(shades)
    for sy in range(size):
        t = min(1, max(0, (sy / scale - 3) / 43))
        draw.line((0, sy, size, sy), fill=(255-int(17*t), 191-int(92*t), 158-int(28*t)))
    shades.putalpha(mask)
    canvas.alpha_composite(shades)
    draw = ImageDraw.Draw(canvas)

    for offset_x, offset_y, radius in ((1,-17,.8), (2.4,-14,.55), (5.9,3,.7), (6.9,5,.65), (-7.1,3,.58)):
        oval(draw, x+offset_x, y+offset_y, radius, radius, '#ee7e77')
    eyeline = y-4
    for eye in (x-5.8, x+1.7):
        if rig['blink']:
            draw.line(coords([(eye-1.45, eyeline+.2), (eye, eyeline-1), (eye+1.45, eyeline+.2)]),
                      fill=ink, width=max(1, round(scale*.8)))
        else:
            oval(draw, eye, eyeline, 2.25, 2.95, '#fff4dc')
            pupil = eye - (1 if rig['glance'] else .55)
            oval(draw, pupil, eyeline+.4, 1.13, 1.65, '#3c243d')
            oval(draw, pupil-.25, eyeline-.22, .38, .50, 'white')
    oval(draw, x-7.7, y+.55, 1.7, .75, '#f17a87')
    oval(draw, x+4.65, y+.55, 1.7, .75, '#f17a87')
    draw.line(coords([(x-3.9, y+.55), (x-2.3, y+1.65), (x-.7, y+.55)]),
              fill=ink, width=max(1, round(scale*.7)))
    if rig.get('sleep'):
        draw.line(coords([(30, 8), (34, 8), (30, 13), (34, 13)]), fill='#a767bf', width=max(1, round(scale)))
    if rig.get('peek'):
        canvas.paste((0, 0, 0, 0), (0, round(22*scale), size, size))
    return canvas
