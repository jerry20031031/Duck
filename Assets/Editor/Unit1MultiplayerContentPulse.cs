using UnityEditor;

// This tiny editor-only type makes Unity re-evaluate the requested UNIT1 build
// after source files are updated while the editor is already open. Keep this
// type in a separate file so Unity always queues a clean editor-script reload.
[InitializeOnLoad]
internal static class Unit1MultiplayerContentPulse
{
    static Unit1MultiplayerContentPulse()
    {
    }
}
