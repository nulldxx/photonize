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

### Simple Example (Old Format)

This is a simplified example showing the basic structure:

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
        "param2": 0.27700871229171753
    }
}
```

### Full Autopilot Example (Current Format)

Real-world example from `--showSettings` with `--upscale` flag showing complete autopilot analysis:

<details>
<summary>Click to expand full JSON structure</summary>

```json
{
    "Enhance": {
        "category": "Enhance",
        "enabled": true,
        "locked": false,
        "model": "High Fidelity V2",
        "params": {
            "height": 1776,
            "mode": "scale",
            "param1": 0.01790519803762436,
            "param2": 0.28788208961486816,
            "param3": 0.17491579055786133,
            "resolution": 72,
            "resolutionUnit": 1,
            "scale": 4,
            "width": 3200
        },
        "subjectSettings": {}
    },
    "Face Recovery": {
        "category": "Face Recovery",
        "enabled": true,
        "locked": false,
        "model": "Face Perfect",
        "params": {
            "creativity": 0,
            "faceOption": "auto",
            "faceParts": ["hair", "necks"],
            "highConfidenceFaces": [0],
            "lowConfidenceFaces": [],
            "param1": 0.800000011920929,
            "selectedFaces": [0],
            "version": 2
        },
        "subjectSettings": {}
    },
    "applyAutopilotOnceCalculated": false,
    "autoPilot": 1,
    "autoPilotSettings": {
        "Denoise": {
            "auto": true,
            "category": "Denoise",
            "deselectedByUpscaling": false,
            "enabled": false,
            "locked": false,
            "mask": true,
            "model": "Low Light Beta",
            "param1": 0.4499095678329468,
            "param2": 0.030233627185225487,
            "params": {
                "auto": true,
                "deselectedByUpscaling": false,
                "locked": false,
                "mask": true,
                "param1": 0.4499095678329468,
                "param2": 0.030233627185225487,
                "recover_detail": 0
            },
            "recover_detail": 0
        },
        "Enhance": {
            "category": "Enhance",
            "enabled": true,
            "height": 1776,
            "locked": false,
            "mode": "scale",
            "model": "High Fidelity V2",
            "param1": 0.01790519803762436,
            "param2": 0.28788208961486816,
            "param3": 0.17491579055786133,
            "params": {
                "height": 1776,
                "locked": false,
                "mode": "scale",
                "param1": 0.01790519803762436,
                "param2": 0.28788208961486816,
                "param3": 0.17491579055786133,
                "resolution": 72,
                "resolutionUnit": 1,
                "scale": 4,
                "width": 3200
            },
            "resolution": 72,
            "resolutionUnit": 1,
            "scale": 4,
            "width": 3200
        },
        "Face Recovery": {
            "category": "Face Recovery",
            "creativity": 0,
            "enabled": true,
            "faceOption": "auto",
            "faceParts": ["hair", "necks"],
            "highConfidenceFaces": [0],
            "locked": false,
            "lowConfidenceFaces": [],
            "param1": 0.800000011920929,
            "params": {
                "creativity": 0,
                "faceOption": "auto",
                "faceParts": ["hair", "necks"],
                "highConfidenceFaces": [0],
                "locked": false,
                "lowConfidenceFaces": [],
                "param1": 0.800000011920929,
                "selectedFaces": [0],
                "version": 2
            },
            "selectedFaces": [0],
            "version": 2
        },
        "Sharpen": {
            "auto": true,
            "category": "Sharpen",
            "compression": 0.4479480981826782,
            "deselectedByUpscaling": false,
            "enabled": false,
            "isLens": false,
            "locked": false,
            "mask": true,
            "model": "Sharpen Standard v2",
            "param1": 0.06499999761581421,
            "param2": 0.2280350774526596,
            "params": {
                "auto": true,
                "compression": 0.4479480981826782,
                "deselectedByUpscaling": false,
                "isLens": false,
                "locked": false,
                "mask": true,
                "param1": 0.06499999761581421,
                "param2": 0.2280350774526596
            }
        }
    },
    "blur_level": -1,
    "noise_level": -1
}
```

</details>

**Key observations from autopilot:**
- Autopilot chooses **4x scale** by default with "High Fidelity V2"
- Face Recovery is **enabled** automatically when faces are detected (`param1=0.8` is the strength)
- Denoise is **disabled** by autopilot (marked as `deselectedByUpscaling`)
- Sharpen is **disabled** by autopilot (marked as `deselectedByUpscaling`)
- The settings contain both top-level enhancement settings and `autoPilotSettings` with what autopilot calculated

## Enhancement Settings

### Upscale (Enhance)

The upscale enhancement is controlled via `--upscale` flag.

**Available Models:**
- Standard
- Standard V2
- High Fidelity
- High Fidelity V2
- Low Resolution

**Autopilot Mode (RECOMMENDED):**
```bash
# Just use --upscale flag without parameters
# Autopilot chooses best settings automatically
tpai.exe --output "C:\output" --upscale input.jpg
```

**JSON Structure (from autopilot):**
```json
"Enhance": {
    "enabled": true,
    "model": "High Fidelity V2",
    "params": {
        "scale": 4,
        "param1": 0.017,  // Minor denoise
        "param2": 0.287,  // Minor deblur
        "param3": 0.174,  // Fix compression
        "mode": "scale",
        "height": 1776,
        "width": 3200
    }
}
```

**Custom Parameters (EXPERIMENTAL - NOT WORKING):**
Attempting to pass custom parameters like `scale=2` or `model="High Fidelity V2"` as command-line arguments causes errors. The CLI appears to require JSON input via `--override`, but the syntax is undocumented.

**Known Issues:**
- CLI does not accept `scale=2 model="High Fidelity V2"` syntax
- Custom settings likely require JSON file (method unknown)
- **Recommendation:** Use autopilot mode instead

### Face Recovery

Face recovery is automatically enabled by autopilot when faces are detected in the image.

**JSON Structure (from autopilot):**
```json
"Face Recovery": {
    "enabled": true,
    "model": "Face Perfect",
    "params": {
        "param1": 0.8,         // Strength (0.0 to 1.0)
        "creativity": 0,        // Creativity level
        "faceOption": "auto",   // Auto-detect faces
        "faceParts": ["hair", "necks"],
        "selectedFaces": [0],   // Which faces to enhance
        "version": 2
    }
}
```

**Autopilot Behavior:**
- **Enabled automatically** when faces are detected
- Uses **"Face Perfect"** model
- Default strength: **0.8** (param1)
- Processes hair and necks in addition to faces
- Version 2 of the face recovery algorithm

**CLI Control:**
Face recovery cannot be explicitly controlled via command-line parameters. It is automatically enabled/disabled by autopilot based on image content.

**Known Issues:**
- No CLI flag to force enable/disable face recovery
- No way to adjust strength or creativity from command line
- Must use autopilot mode (which is actually good - it works well!)

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

## Photonize Implementation

The current Photonize Windows application uses the following approach:

1. **Custom 2x Upscale with JSON Settings**: Creates a JSON settings file with:
   - **2x scale** (instead of autopilot's 4x)
   - **High Fidelity V2** model
   - Uses `--override` flag to apply custom settings
   - Settings file is created temporarily and deleted after processing

2. **Settings JSON Structure**:
   ```json
   {
       "Enhance": {
           "enabled": true,
           "category": "Enhance",
           "locked": false,
           "model": "High Fidelity V2",
           "params": {
               "scale": 2,
               "mode": "scale",
               "param1": 0.0,
               "param2": 0.0,
               "param3": 0.0
           }
       }
   }
   ```

3. **Format Preservation**: Uses `--format` to preserve original format (jpg/png/etc)

4. **Post-Processing**: After Topaz completes:
   - Loads the output file using ImageSharp
   - Converts to WebP (quality 90, lossy)
   - Deletes the intermediate Topaz output file
   - Final output: WebP files in `TopazUpscaled` folder

5. **Error Handling**: Ignores exit code, checks if output file was created successfully

This approach gives us:
- ✅ **2x upscale** (more practical than autopilot's 4x)
- ✅ High-quality upscaling with High Fidelity V2 model
- ✅ WebP output for better file sizes
- ✅ Reliable operation despite tpai.exe crashes
- ⚠️ Face recovery and other enhancements are NOT included in custom settings (only upscale)

## Known Issues

1. **Exit Code C0000409**: tpai.exe frequently crashes with exit code -1073740791 (0xC0000409) even after successful processing.
   - **Solution**: Check if output file was created instead of relying on exit code
   - **Status**: Handled in Photonize implementation

2. **WebP Not Supported**: Despite being a modern format, tpai.exe does not support WebP output.
   - **Solution**: Convert manually after processing using ImageSharp
   - **Status**: Implemented in Photonize

3. **Custom Settings via Command Line Parameters**: CLI does not accept custom parameters like `scale=2 model="High Fidelity V2"` directly
   - **Solution**: Create a JSON settings file and use `--override <path-to-json>`
   - **Status**: Implemented in Photonize - creates temporary JSON file with 2x upscale settings

## Example Commands

### Basic upscale with autopilot (RECOMMENDED)
```bash
# Autopilot automatically chooses 4x scale with High Fidelity V2
# Also enables face recovery if faces are detected
tpai.exe --output "C:\output" --upscale input.jpg
```

**Note:** The `--upscale` flag by itself enables autopilot upscaling. Autopilot will:
- Choose an appropriate scale (typically 4x)
- Select the best model (typically "High Fidelity V2")
- Enable face recovery if faces are detected
- Optimize param1, param2, param3 values based on image analysis

### View autopilot settings without processing
```bash
# See what autopilot would do without actually processing
tpai.exe --upscale --showSettings --skipProcessing input.jpg
```

### Preserve original format
```bash
# Keep original file extension (jpg stays jpg, png stays png)
tpai.exe --output "C:\output" --format preserve --upscale input.jpg
```

### Output as PNG with compression
```bash
# Convert to PNG with compression level 5 (0-10 scale)
tpai.exe --output "C:\output" --format png --compression 5 --upscale input.jpg
```

### Custom settings with JSON file (WORKING!)
```bash
# Create a settings.json file with your desired configuration
# Example: settings.json
# {
#     "Enhance": {
#         "enabled": true,
#         "category": "Enhance",
#         "locked": false,
#         "model": "High Fidelity V2",
#         "params": {
#             "scale": 2,
#             "mode": "scale",
#             "param1": 0.0,
#             "param2": 0.0,
#             "param3": 0.0
#         }
#     }
# }

# Then use --override flag with the JSON file path
tpai.exe --output "C:\output" --override "settings.json" input.jpg
```

**Important Notes:**
- The `--override` flag **requires a JSON file path**, not inline parameters
- This syntax does **NOT** work: `--upscale scale=2 model="High Fidelity V2"`
- You must create a JSON file and pass its path
- Settings structure must match the format shown in autopilot examples
- Photonize automatically creates and manages this JSON file

### Process multiple files
```bash
# Process all JPG files in a directory
tpai.exe --output "C:\output" --upscale "C:\photos\*.jpg"

# Recursively process subdirectories
tpai.exe --output "C:\output" --recursive --upscale "C:\photos"
```
