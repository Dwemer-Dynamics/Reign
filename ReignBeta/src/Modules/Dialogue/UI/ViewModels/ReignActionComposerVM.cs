using System;
using System.Collections.Generic;
using Reign.Core.Contracts.Actions;

namespace ReignBeta.UI.ViewModels
{
    // The widget receives only this opaque, local UI identity. Executable terms never
    // come from an editable string, pasted markup, or a widget-supplied agreement.
    public sealed class ReignActionComposerVM : IDisposable
    {
        private static readonly Dictionary<string, WeakReference<ReignActionComposerVM>> Instances = new Dictionary<string, WeakReference<ReignActionComposerVM>>();
        private readonly Action _changed;
        public string Id { get; } = Guid.NewGuid().ToString("N");
        public ActionComposer State { get; } = new ActionComposer();
        public ReignActionComposerVM(Action changed) { _changed = changed; Instances[Id] = new WeakReference<ReignActionComposerVM>(this); }
        public static ReignActionComposerVM Find(string id) => id != null && Instances.TryGetValue(id, out var weak) && weak.TryGetTarget(out var vm) ? vm : null;
        public void SetText(string value) { if (State.Text == (value ?? "")) return; State.SetText(value); _changed(); }
        public void Insert(ActionAgreement draft) { State.Insert(draft); _changed(); }
        public void Replace(int start, int length, string value) { State.Replace(start, length, value); _changed(); }
        public void Dispose() { Instances.Remove(Id); }
    }
}
