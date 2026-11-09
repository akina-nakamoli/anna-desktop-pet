"""Offline Japanese text in (UTF-8), PCM WAV out."""
import io
import sys
import unicodedata
import wave
from pathlib import Path

import numpy as np

# Use the native frontend and HTS engine without the optional reading models.
package = Path(__file__).resolve().parent / "pyopenjtalk"
sys.path.insert(0, str(package))
from htsengine import HTSEngine
from openjtalk import OpenJTalk


def main():
    text = unicodedata.normalize("NFKC", sys.stdin.buffer.read(4096).decode("utf-8")).strip()
    if not text:
        raise ValueError("No speech text")
    frontend = OpenJTalk(b"pyopenjtalk/dictionary")
    engine = HTSEngine(b"pyopenjtalk/htsvoice/mei_normal.htsvoice")
    labels = frontend.make_label(frontend.run_frontend(text))
    engine.set_speed(float(sys.argv[1]))
    samples = engine.synthesize(labels)
    if len(samples) == 0:
        raise ValueError("Empty speech")
    with io.BytesIO() as buffer:
        with wave.open(buffer, "wb") as output:
            output.setparams((1, 2, engine.get_sampling_frequency(), 0, "NONE", "not compressed"))
            output.writeframes(np.clip(samples, -32768, 32767).astype("<i2").tobytes())
        sys.stdout.buffer.write(buffer.getvalue())


if __name__ == "__main__":
    main()
