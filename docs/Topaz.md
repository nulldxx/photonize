# Topaz Photo AI CLI Documentation

This document contains reference information for using Topaz Photo AI's command-line interface (`tpai.exe`).

## Installation Path

Default installation: `C:\Program Files\Topaz Labs LLC\Topaz Photo AI\tpai.exe`

## Command Line Help

```
Options:
    --output, -o: Output folder to save images to. If it doesn't exist the program will attempt to create it.
    --overwrite: Allow overwriting of files. THIS IS DESTRUCTIVE.
    --recursive, -r: If given a folder path, it will recurse into subdirectories instead of just grabbing top level files.
        Note: If output folder is specified, the input folder's structure will be recreated within the output as necessary.

File Format Options:
    --format, -f: Set the output format. Accepts jpg, jpeg, png, tif, tiff, dng, or preserve. Default: preserve
        Note: Preserve will attempt to preserve the exact input extension, but RAW files will still be converted to DNG.
        Note: webp is NOT supported - must convert manually after processing

Format Specific Options:
    --quality, -q: JPEG quality for output. Must be between 0 and 100. Default: 95
    --compression, -c: PNG compression amount. Must be between 0 and 10. Default: 2
    --bit-depth, -d: TIFF bit depth. Must be either 8 or 16. Default: 16
    --tiff-compression: -tc: TIFF compression format. Must be "none", "lzw", or "zip".
        Note: lzw is not allowed on 16-bit output and will be converted to zip.

Debug Options:
    --showSettings: Shows the Autopilot settings for images before they are processed
    --skipProcessing: Skips processing the image (e.g., if you just want to know the settings)
    --verbose, -v: Print more log entries to console.

Settings Options:
    Note: EXPERIMENTAL. The API for changing the processing settings is experimental and subject to change.
          After enabling an enhancement you may specify settings to override.
    --showSettings: Prints out the final settings used when processing.
    --override: If specified, any model settings will fully replace Autopilot settings.
                Default behavior will merge other options into Autopilot settings.
    --upscale: Turn on the Upscale enhancement. Pass enabled=false to turn it off instead.
    --noise: Turn on the Denoise enhancement. Pass enabled=false to turn it off instead.
    --sharpen: Turn on the Sharpen enhancement. Pass enabled=false to turn it off instead.
    --lighting: Turn on the Adjust lighting enhancement. Pass enabled=false to turn it off instead.
    --color: Turn on the Balance color enhancement. Pass enabled=false to turn it off instead.

Return values:
    0 - Success
    1 - Partial Success (e.g., some files failed)
    -1 (255) - No valid files passed.
    -2 (254) - Invalid log token. Open the app normally to login.
    -3 (253) - An invalid argument was found.
```

## Autopilot Settings JSON Structure

Example output from `--showSettings` showing the structure of processing settings:

```json
{
    "Denoise": {
        "auto": true,
        "enabled": true,
        "model": "Low Light",
        "param1": 0.2718254327774048,
        "param2": 0.2022935003042221
    },
    "Enhance": {
        "enabled": false,
        "model": "High Fidelity",
        "param1": 0.002386240754276514,
        "param2": 0.19391538027960592,
        "param3": 0.7014735341072083,
        "scale": 4
    },
    "Face Recovery": {
        "auto": true,
        "enabled": false,
        "param1": 0.800000011920929,
        "selectedFaces": null
    },
    "Sharpen": {
        "auto": true,
        "compression": 0.49727872014045715,
        "enabled": false,
        "isLens": false,
        "mask": false,
        "model": "Sharpen Motionblur",
        "param1": 0,
        "param2": 0.27700871229171753,
        "slens_blur": 0,
        "slens_noise": 0,
        "smb_blur": 0.27700871229171753,
        "smb_noise": 0,
        "smot_blur": 0.2022935003042221,
        "smot_noise": 0,
        "stg_blur": 0.03999999910593033,
        "stg_noise": 0.05
    }
}
```

## Enhancement Settings

### Upscale (Enhance)

The upscale enhancement is controlled via `--upscale` flag.

**Available Models:**
- Standard
- Standard V2
- High Fidelity
- High Fidelity V2
- Low Resolution

**Parameters:**
- `scale`: Upscale factor (e.g., 2 for 2x, 4 for 4x)
- `model`: Model name (see list above)
- `param1`: Purpose unknown (denoise-related?)
- `param2`: Purpose unknown (deblur-related?)
- `param3`: Purpose unknown (compression artifact fix?)

**Example usage:**
```bash
tpai.exe --output "C:\output" --upscale scale=2 model="High Fidelity V2" input.jpg
```

### Face Recovery

The face recovery enhancement improves facial details.

**Parameters:**
- `enabled`: true/false
- `auto`: true/false (let autopilot decide)
- `param1`: Strength (0.0 to 1.0, default appears to be ~0.8)

**Example usage:**
```bash
tpai.exe --output "C:\output" --upscale scale=2 --face-recovery enabled=true param1=0.8 input.jpg
```

Note: The exact CLI parameter name for face recovery is unknown. It might be:
- `--face-recovery`
- `--recover-faces`
- Or it might only be available through the GUI

### Denoise

Controlled via `--noise` flag.

**Available Models:**
- Low Light
- (Others unknown)

**Parameters:**
- `param1`: Unknown purpose
- `param2`: Unknown purpose

### Sharpen

Controlled via `--sharpen` flag.

**Available Models:**
- Sharpen Standard
- Sharpen Strong
- Sharpen Motionblur
- Sharpen Lens Blur
- (Others unknown)

**Parameters:**
- `compression`: Compression artifact handling (0.0 to 1.0)
- `param1`: Strength
- `param2`: Additional parameter (blur-specific?)

## Known Issues

1. **Exit Code C0000409**: tpai.exe frequently crashes with exit code -1073740791 (0xC0000409) even after successful processing. Solution: Check if output file was created instead of relying on exit code.

2. **WebP Not Supported**: Despite being a modern format, tpai.exe does not support WebP output. Must convert manually after processing.

## Example Commands

### Basic 2x upscale with autopilot
```bash
tpai.exe --output "C:\output" --upscale input.jpg
```

### 2x upscale with High Fidelity V2 model
```bash
tpai.exe --output "C:\output" --upscale scale=2 model="High Fidelity V2" input.jpg
```

### Disable autopilot, use only manual settings
```bash
tpai.exe --output "C:\output" --override --upscale scale=2 model="High Fidelity V2" input.jpg
```

### View settings without processing
```bash
tpai.exe --showSettings --skipProcessing input.jpg
```
