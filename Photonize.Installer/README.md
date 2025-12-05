# Photoize WiX Installer

This project creates a Windows Installer (.msi) for the Photoize application.

## Prerequisites

You need to install the WiX Toolset to build the installer:

1. **WiX Toolset v3.11 or later**
   - Download from: https://wixtoolset.org/releases/
   - Or use dotnet tool: `dotnet tool install --global wix`

2. **WiX Visual Studio Extension** (if using Visual Studio)
   - Download from: https://marketplace.visualstudio.com/items?itemName=WixToolset.WixToolsetVisualStudio2022Extension

## Building the Installer

### Using Visual Studio

1. Open `Photonize.sln`
2. Set build configuration to **Release|x64**
3. Right-click on `Photonize.Installer` project
4. Select **Build**
5. The installer will be created at: `Photonize.Installer\bin\x64\Release\Photonize.msi`

### Using Command Line

```bash
# Build the main application first
cd Photonize
dotnet build -c Release

# Build the installer
cd ../Photonize.Installer
msbuild Photonize.Installer.wixproj /p:Configuration=Release /p:Platform=x64
```

## Installer Features

The installer includes:

- **Installation Location**: `C:\Program Files\Photonize\`
- **Start Menu Shortcut**: Creates a shortcut in the Start Menu under "Photoize"
- **Desktop Shortcut**: Optional desktop shortcut (user can choose during installation)
- **Uninstaller**: Available through Windows Settings > Apps & features
- **Upgrade Support**: Automatically handles upgrades from previous versions

## What Gets Installed

- Photonize.exe (main executable)
- Photonize.dll
- Required NuGet dependencies:
  - Newtonsoft.Json.dll
  - CommunityToolkit.Mvvm.dll
  - GongSolutions.WPF.DragDrop.dll
- Runtime configuration files

## Customization

### Icon

Replace `Photonize.ico` with your custom application icon. The current file is a placeholder.

### License

Edit `License.rtf` to customize the license agreement shown during installation.

### Version Number

Update the version in `Product.wxs`:
```xml
<?define ProductVersion="1.0.0.0" ?>
```

### Product Information

Edit these defines in `Product.wxs`:
```xml
<?define ProductName="Photoize" ?>
<?define Manufacturer="Photoize" ?>
```

## Troubleshooting

### "WiX Toolset not found" error
- Install WiX Toolset from https://wixtoolset.org/releases/
- Restart Visual Studio after installation

### Missing DLL files during build
- Ensure Photonize project is built in Release mode first
- Check that all NuGet packages are restored

### Installer doesn't include all dependencies
- Check the `Product.wxs` file and add any missing Component entries
- Use `heat.exe` from WiX Toolset to auto-harvest files if needed

## Distribution

After building, distribute the `.msi` file to users. They can install by:
1. Double-clicking the `.msi` file
2. Following the installation wizard
3. Choosing installation location and options

## Notes

- The installer requires administrator privileges to install to Program Files
- The UpgradeCode GUID should never change - it allows upgrades to work properly
- Each build gets a unique ProductCode (using `Id="*"`)
- .NET 8 Desktop Runtime must be installed on the target system
