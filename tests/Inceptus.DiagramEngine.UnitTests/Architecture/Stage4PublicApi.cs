using System.Collections.Immutable;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

// Exact additive Stage 4 API delta from the committed Stage 3 baseline. No existing
// signature or metadata entry is removed or replaced.
internal static class Stage4PublicApi
{
    internal static ImmutableArray<string> Added { get; } =
    [
        "F Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerOperation Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerOperation ApplicationAction 8 ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction Public, HideBySig, SpecialName get_DisabledExplanation System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction Public, HideBySig, SpecialName get_Id System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction Public, HideBySig, SpecialName get_IsEnabled System.Boolean ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction Public, HideBySig, SpecialName get_Label System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:System.String:id::;None:System.String:label::;Optional, HasDefault:System.Boolean:isEnabled:True:[System.Runtime.InteropServices.OptionalAttribute()];Optional, HasDefault:System.String:disabledExplanation:null:[System.Runtime.CompilerServices.NullableAttribute((Byte)2)];[System.Runtime.InteropServices.OptionalAttribute()])  ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Public, HideBySig, SpecialName get_Part Inceptus.DiagramEngine.Bpmn.Blazor.BpmnPresentationPart ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Public, HideBySig, SpecialName get_Presentation Inceptus.DiagramEngine.Bpmn.Blazor.BpmnPresentationSnapshot ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Public, HideBySig, SpecialName get_RequestVersion System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Public, HideBySig, SpecialName get_Stamp Inceptus.DiagramEngine.Bpmn.Blazor.BpmnPresentationContextStamp ()  ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Public, HideBySig, SpecialName get_Target Inceptus.DiagramEngine.Bpmn.Blazor.BpmnSemanticSelectionTarget ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs Public, HideBySig, SpecialName get_ActionId System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs Public, HideBySig, SpecialName get_CancellationToken System.Threading.CancellationToken ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs Public, HideBySig, SpecialName get_Context Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:System.Object:object::;None:System.IntPtr:method::)  ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider Public, Virtual, HideBySig, VtableLayoutMask BeginInvoke System.IAsyncResult (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext:context::[System.Runtime.CompilerServices.NullableAttribute((Byte)1)];None:System.Threading.CancellationToken:cancellationToken::;None:System.AsyncCallback:callback::;None:System.Object:object::)  ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider Public, Virtual, HideBySig, VtableLayoutMask EndInvoke System.Threading.Tasks.ValueTask`1<System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction>>[System.Runtime.CompilerServices.NullableAttribute(new Byte[3] { 0, 1, 1 })] (None:System.IAsyncResult:result::)  ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider Public, Virtual, HideBySig, VtableLayoutMask Invoke System.Threading.Tasks.ValueTask`1<System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction>>[System.Runtime.CompilerServices.NullableAttribute(new Byte[3] { 0, 1, 1 })] (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext:context::;None:System.Threading.CancellationToken:cancellationToken::)  [System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig, SpecialName get_ApplicationActionInvoked Microsoft.AspNetCore.Components.EventCallback`1<Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig, SpecialName get_ApplicationActionProvider Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig, SpecialName set_ApplicationActionInvoked System.Void (None:Microsoft.AspNetCore.Components.EventCallback`1<Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs>:value::[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })])  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig, SpecialName set_ApplicationActionProvider System.Void (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider:value::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction System.Boolean IsEnabled ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction System.String DisabledExplanation [System.Runtime.CompilerServices.NullableAttribute((Byte)2)]",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction System.String Id ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction System.String Label ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Inceptus.DiagramEngine.Bpmn.Blazor.BpmnPresentationContextStamp Stamp ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Inceptus.DiagramEngine.Bpmn.Blazor.BpmnPresentationPart Part [System.Runtime.CompilerServices.NullableAttribute((Byte)2)]",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Inceptus.DiagramEngine.Bpmn.Blazor.BpmnPresentationSnapshot Presentation ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Inceptus.DiagramEngine.Bpmn.Blazor.BpmnSemanticSelectionTarget Target ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext System.Int64 RequestVersion ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Context ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs System.String ActionId ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs System.Threading.CancellationToken CancellationToken ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider ApplicationActionProvider [Microsoft.AspNetCore.Components.ParameterAttribute()];[System.Runtime.CompilerServices.NullableAttribute((Byte)2)]",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Microsoft.AspNetCore.Components.EventCallback`1<Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs> ApplicationActionInvoked [Microsoft.AspNetCore.Components.ParameterAttribute()];[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "T Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationAction Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionContext Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionInvocationEventArgs Public, Sealed, BeforeFieldInit : System.EventArgs  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Bpmn.Blazor.BpmnApplicationActionProvider Public, Sealed : System.MulticastDelegate System.ICloneable;System.Runtime.Serialization.ISerializable ",
    ];
}
