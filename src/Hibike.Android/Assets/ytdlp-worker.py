"""Persistent yt-dlp host for Hibike on Android.

Starting the bundled Python and importing yt-dlp costs seconds on a phone and was paid on every
search and every track. This host imports yt-dlp once and runs each request through yt-dlp's own
command-line entry point, so arguments, output and exit codes match a separate process exactly.

Protocol: one JSON object per line on stdin, {"args": [...]}; one JSON object per line on the
original stdout, {"exit": int, "stdout": str, "stderr": str}. The first line written is
{"ready": true} once yt-dlp has been imported. Requests are handled one at a time.
"""
import io
import json
import sys
import traceback

sys.path.insert(0, sys.argv[1])
import yt_dlp  # noqa: E402  (path set above)

_protocol = sys.stdout.buffer
_requests = sys.stdin.buffer


def _send(message):
    _protocol.write(json.dumps(message).encode('ascii') + b'\n')
    _protocol.flush()


def _capture():
    # yt-dlp writes encoded bytes to `.buffer` when present, so give it a real byte buffer.
    return io.TextIOWrapper(io.BytesIO(), encoding='utf-8', errors='replace', write_through=True)


def _text(stream):
    stream.flush()
    return stream.buffer.getvalue().decode('utf-8', errors='replace')


def _run(args):
    out, err = _capture(), _capture()
    saved = sys.stdout, sys.stderr
    sys.stdout, sys.stderr = out, err
    code = 0
    try:
        yt_dlp.main(args)
    except SystemExit as stop:
        if stop.code is None:
            code = 0
        elif isinstance(stop.code, int):
            code = stop.code
        else:
            # Same as the interpreter: a message exit prints it and returns status 1.
            err.write(str(stop.code) + '\n')
            code = 1
    except BaseException:  # noqa: BLE001  (a failed request must not end the host)
        err.write(traceback.format_exc())
        code = 1
    finally:
        sys.stdout, sys.stderr = saved
    return {'exit': code, 'stdout': _text(out), 'stderr': _text(err)}


_send({'ready': True})
for line in _requests:
    if not line.strip():
        continue
    try:
        request = json.loads(line)
        args = request['args']
        if not isinstance(args, list) or not all(isinstance(item, str) for item in args):
            raise ValueError('args must be a list of strings')
    except (ValueError, KeyError, TypeError) as error:
        _send({'exit': 2, 'stdout': '', 'stderr': 'ERROR: invalid worker request: %s\n' % error})
        continue
    _send(_run(args))
