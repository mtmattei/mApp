#!/usr/bin/env bash
# Records one scenario against the running app on :99 with real X input. Writes raw.mp4 and captions.tsv.
export DISPLAY=:99
cd "$(dirname "$0")"
rm -f raw.mp4 captions.tsv
ffmpeg -loglevel error -y -f x11grab -draw_mouse 1 -framerate 30 -video_size 1440x900 -i :99 -c:v libx264 -preset veryfast -crf 18 -pix_fmt yuv420p raw.mp4 &
FF=$!
T0=$(date +%s.%N)
cap() { printf "%s\t%s\n" "$(echo "$(date +%s.%N) - $T0" | bc)" "$1" >> captions.tsv; }
glide() { # eased move to x y over ~0.6 s
  eval "$(xdotool getmouselocation --shell)"; local x0=$X y0=$Y n=24
  for i in $(seq 1 $n); do
    local t=$(echo "s=$i/$n; s*s*(3-2*s)" | bc -l)
    xdotool mousemove "$(echo "$x0+($1-$x0)*$t" | bc | cut -d. -f1)" "$(echo "$y0+($2-$y0)*$t" | bc | cut -d. -f1)"; sleep 0.025
  done
}
click() { glide "$1" "$2"; sleep 0.25; xdotool click 1; }

xdotool mousemove 720 760
sleep 0.8
cap "QA ticket: “Place order stays greyed out on Checkout, even with items in the cart.” You’re new to this codebase."
sleep 4.5
cap "Start from the app instead of the file tree. Checkout lives under Purchase."
glide 600 470; sleep 0.6; click 660 348
sleep 3.2
cap "Cart and Checkout share one view model. Open Checkout."
sleep 2.4; click 815 282
sleep 2.4
cap "The Behavior lens draws every binding from the screen to the view model."
sleep 0.6; click 1028 24
sleep 3
glide 560 557; sleep 0.5; glide 700 568; sleep 0.4
cap "Place order’s IsEnabled is bound to CanPlaceOrder."
glide 820 593; sleep 1.6; click 845 593
sleep 2.2
cap "Three conditions: items in the cart, an address of at least 8 characters, no order submitting. The source line is already open."
glide 1000 470; sleep 1.2; glide 150 640; sleep 2.6; glide 1260 300; sleep 2.4
cap "Where does Address come from?"
click 990 470
sleep 2.4
cap "Only the Address box feeds it, and nothing on the screen mentions the 8-character rule."
glide 425 525; sleep 1.6; glide 1230 416; sleep 3
cap "Back along the trail with Alt+←."
sleep 0.8; xdotool key alt+Left
sleep 2
cap "One dependent: only Place order reads this rule, so changing it won’t touch the Cart screen."
glide 590 72; sleep 1.2; glide 1190 597; sleep 3.4
cap "Dock the viewer to read the whole picture."
glide 720 70; sleep 0.4; xdotool mousedown 1
for i in $(seq 1 30); do xdotool mousemove $((720+i*18)) $((70+i*24)); sleep 0.03; done
sleep 0.4; xdotool mouseup 1
sleep 2.6
cap "Diagnosis without opening a file: the rule works, the UI never explains it. The fix is a hint under the address box."
glide 1000 160; sleep 5.5
cap ""
sleep 0.5
kill -INT $FF; wait $FF 2>/dev/null
echo "recorded $(echo "$(date +%s.%N) - $T0" | bc | cut -d. -f1)s"
