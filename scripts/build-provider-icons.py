"""Render flat provider ICOs using system librsvg/Cairo (build-time only).

Source vectors and their MIT license live in Data/Branding/Providers.
Run on Linux with librsvg-2 and Cairo installed; no Python packages needed.
"""
from pathlib import Path
import ctypes as c
import runpy
import struct

ROOT = Path(__file__).resolve().parent.parent
helpers = runpy.run_path(str(ROOT / 'scripts/build-tray-icons.py'))
dib, png = helpers['dib'], helpers['png']
rsvg = c.CDLL('librsvg-2.so.2'); cairo = c.CDLL('libcairo.so.2'); objects = c.CDLL('libgobject-2.0.so.0')
rsvg.rsvg_handle_new_from_data.argtypes = [c.c_char_p, c.c_size_t, c.POINTER(c.c_void_p)]
rsvg.rsvg_handle_new_from_data.restype = c.c_void_p
class Viewport(c.Structure):
    _fields_ = [('x', c.c_double), ('y', c.c_double), ('width', c.c_double), ('height', c.c_double)]
rsvg.rsvg_handle_render_document.argtypes = [c.c_void_p, c.c_void_p, c.POINTER(Viewport), c.POINTER(c.c_void_p)]
rsvg.rsvg_handle_render_document.restype = c.c_int
cairo.cairo_image_surface_create.argtypes = [c.c_int, c.c_int, c.c_int]; cairo.cairo_image_surface_create.restype = c.c_void_p
cairo.cairo_create.argtypes = [c.c_void_p]; cairo.cairo_create.restype = c.c_void_p
cairo.cairo_surface_flush.argtypes = [c.c_void_p]
cairo.cairo_image_surface_get_data.argtypes = [c.c_void_p]; cairo.cairo_image_surface_get_data.restype = c.POINTER(c.c_ubyte)
cairo.cairo_image_surface_get_stride.argtypes = [c.c_void_p]; cairo.cairo_image_surface_get_stride.restype = c.c_int
cairo.cairo_destroy.argtypes = [c.c_void_p]; cairo.cairo_surface_destroy.argtypes = [c.c_void_p]
objects.g_object_unref.argtypes = [c.c_void_p]


def raster(source, size, color):
    data = source.replace('currentColor', color).encode()
    error = c.c_void_p()
    handle = rsvg.rsvg_handle_new_from_data(data, len(data), c.byref(error))
    if not handle: raise RuntimeError('Cannot read provider SVG')
    surface = cairo.cairo_image_surface_create(0, size, size)
    dc = cairo.cairo_create(surface)
    try:
        inset = size / 20
        if not rsvg.rsvg_handle_render_document(handle, dc, c.byref(Viewport(inset, inset, size - 2 * inset, size - 2 * inset)), c.byref(error)):
            raise RuntimeError('Cannot render provider SVG')
        cairo.cairo_surface_flush(surface)
        pixels = cairo.cairo_image_surface_get_data(surface)
        stride = cairo.cairo_image_surface_get_stride(surface)
        rgba = bytearray()
        for y in range(size):
            for x in range(size):
                b, g, r, a = pixels[y * stride + x * 4:y * stride + x * 4 + 4]
                rgba.extend((*(min(255, round(v * 255 / a)) if a else 0 for v in (r, g, b)), a))
        assert rgba[3] == rgba[-1] == 0, 'Transparent corners required'
        assert any(a >= 128 for a in rgba[3::4]), 'Missing visible logo'
        return bytes(rgba)
    finally:
        cairo.cairo_destroy(dc); cairo.cairo_surface_destroy(surface); objects.g_object_unref(handle)


def build():
    sizes = (16, 20, 24, 32)
    for name in ('openai', 'claude', 'gemini'):
        source = (ROOT / f'Data/Branding/Providers/{name}.svg').read_text()
        for theme, color in (('light', '#182230'), ('dark', '#F2F5FA')):
            frames = [dib(size, raster(source, size, color)) for size in sizes]
            directory = bytearray(struct.pack('<HHH', 0, 1, len(sizes))); offset = 6 + 16 * len(sizes)
            for size, frame in zip(sizes, frames):
                directory.extend(struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32, len(frame), offset)); offset += len(frame)
            (ROOT / f'src/SharpVoice.UI/Assets/provider-{name}-{theme}.ico').write_bytes(directory + b''.join(frames))
    print('Built six flat, transparent provider icons at 16, 20, 24 and 32 px.')


if __name__ == '__main__':
    build()
