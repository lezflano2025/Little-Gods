extends Node

# Smoke test — verifies GDScript loads and compiles in M0.
# Should be removed once a real scene exists.

func _ready() -> void:
	print("[GDScript] SmokeTest._ready — Little Gods M0 boot")
	# Exit immediately when run headless via --quit-after.
