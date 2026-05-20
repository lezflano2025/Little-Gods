#!/usr/bin/env -S godot --headless --quit-after 120 -s
# tools/diff_images.gd - compute per-pixel diff between two PNGs.
# Usage (via tools/snapshot-diff.ps1):
#   godot --headless -s res://tools/diff_images.gd -- <golden> <actual> <diff> <tol>
#
# Writes a side-by-side diff PNG to <diff>, prints one line:
#   [diff] frac=<f> diff_pixels=<n> total=<n> name=<base>
#
# Exits 0 always; the wrapper script decides pass/fail from the printed fraction.

extends SceneTree


func _init() -> void:
	var raw_args := OS.get_cmdline_user_args()
	if raw_args.size() < 4:
		printerr("[diff] expected: golden actual diff_out channel_tol")
		quit(1)
		return

	var golden_path: String = raw_args[0]
	var actual_path: String = raw_args[1]
	var diff_path: String   = raw_args[2]
	var tol: int            = int(raw_args[3])

	var golden := Image.load_from_file(ProjectSettings.globalize_path(golden_path))
	var actual := Image.load_from_file(ProjectSettings.globalize_path(actual_path))

	if golden == null:
		printerr("[diff] could not load golden: %s" % golden_path)
		quit(1)
		return
	if actual == null:
		printerr("[diff] could not load actual: %s" % actual_path)
		quit(1)
		return

	if golden.get_size() != actual.get_size():
		printerr("[diff] size mismatch: golden=%s actual=%s" % [golden.get_size(), actual.get_size()])
		quit(1)
		return

	var w: int = golden.get_width()
	var h: int = golden.get_height()
	var total: int = w * h
	var differing: int = 0

	var diff_img := Image.create(w, h, false, Image.FORMAT_RGB8)
	for y in range(h):
		for x in range(w):
			var gc := golden.get_pixel(x, y)
			var ac := actual.get_pixel(x, y)
			var dr: int = int(abs(gc.r - ac.r) * 255.0)
			var dg: int = int(abs(gc.g - ac.g) * 255.0)
			var db: int = int(abs(gc.b - ac.b) * 255.0)
			var max_d: int = max(dr, max(dg, db))
			if max_d > tol:
				differing += 1
				diff_img.set_pixel(x, y, Color(1, 0, 0))   # mark diff in red
			else:
				diff_img.set_pixel(x, y, Color(gc.r * 0.3, gc.g * 0.3, gc.b * 0.3))

	var diff_abs := ProjectSettings.globalize_path(diff_path)
	DirAccess.make_dir_recursive_absolute(diff_abs.get_base_dir())
	diff_img.save_png(diff_abs)

	var frac: float = float(differing) / float(total)
	var base := diff_path.get_file()
	print("[diff] frac=%f diff_pixels=%d total=%d name=%s" % [frac, differing, total, base])
	quit(0)
