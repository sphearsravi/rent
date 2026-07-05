namespace RentCollection.Web;

/// <summary>
/// Marker type for the shared localization resource. UI strings across all
/// views resolve against Resources/SharedResource.&lt;culture&gt;.resx, keyed by
/// their English text (so English needs no resource file — it's the fallback).
/// Injected app-wide in _ViewImports as IHtmlLocalizer&lt;SharedResource&gt; L.
/// </summary>
public class SharedResource
{
}
