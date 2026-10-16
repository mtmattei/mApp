"""captions.tsv (seconds<TAB>text, one row per caption, last row empty) -> captions.ass, then burn in:
   python3 captions.py && ffmpeg -i raw.mp4 -vf ass=captions.ass -c:v libx264 -crf 22 -pix_fmt yuv420p ../../media/app-orbit-scenario.mp4"""
import os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
rows = [line.rstrip("\n").split("\t") for line in open("captions.tsv", encoding="utf-8")]
rows = [(float(t), text) for t, text in rows]

def ts(s):
    return f"{int(s // 3600)}:{int(s % 3600 // 60):02d}:{s % 60:05.2f}"

head = """[Script Info]
ScriptType: v4.00+
PlayResX: 1440
PlayResY: 900
WrapStyle: 0
ScaledBorderAndShadow: yes

[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Cap,DejaVu Sans,25,&H00FFFFFF,&H00FFFFFF,&H1A1F1A17,&H1A1F1A17,0,0,0,0,100,100,0,0,3,14,0,2,300,300,48,1

[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
"""
events = [
    f"Dialogue: 0,{ts(max(0, t - 0.1))},{ts(rows[i + 1][0] - 0.05)},Cap,,0,0,0,,{{\\fad(150,120)}}{text}"
    for i, (t, text) in enumerate(rows[:-1]) if text
]
open("captions.ass", "w", encoding="utf-8").write(head + "\n".join(events) + "\n")
print(f"{len(events)} captions")
