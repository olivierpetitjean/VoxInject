// Explicit global aliases to resolve ambiguities between WPF and WinForms
// when both are referenced in the same project.
global using Application      = System.Windows.Application;
global using Color            = System.Windows.Media.Color;
global using ComboBox         = System.Windows.Controls.ComboBox;
global using KeyEventArgs     = System.Windows.Input.KeyEventArgs;
global using MessageBox       = System.Windows.MessageBox;
global using StartupEventArgs = System.Windows.StartupEventArgs;
global using ExitEventArgs    = System.Windows.ExitEventArgs;
global using Clipboard        = System.Windows.Clipboard;

// System.Windows.Shapes.Path shadows System.IO.Path via WPF implicit usings
global using File             = System.IO.File;
global using Directory        = System.IO.Directory;
global using Path             = System.IO.Path;
