using System.Reflection;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class InceptusBpmnModelerFacadeComponentTests
{
    [Fact]
    public void SmartActionPublicDefaultsAndOperationKeepThePresentationInterfaceUnchanged()
    {
        using var component = new InceptusBpmnModeler();
        Assert.False(component.ApplicationContextualActionsEnabled);
        Assert.Equal(0, component.ApplicationActionsRevision);
        Assert.Null(component.ApplicationActionHandler);
        Assert.Null(component.ApplicationActionProvider);
        Assert.False(component.ApplicationActionInvoked.HasDelegate);
        foreach (var name in new[] { nameof(component.ApplicationContextualActionsEnabled),
            nameof(component.ApplicationActionsRevision), nameof(component.ApplicationActionHandler) })
            Assert.NotNull(typeof(InceptusBpmnModeler).GetProperty(name)!.GetCustomAttribute<ParameterAttribute>());
        var operation = typeof(InceptusBpmnModeler).GetMethod(nameof(component.RequestApplicationContextualActionsAsync))!;
        Assert.Equal(typeof(ValueTask<BpmnPresentationOperationResult>), operation.ReturnType);
        Assert.Equal(new[] { typeof(BpmnPresentationContextStamp), typeof(long), typeof(CancellationToken) },
            operation.GetParameters().Select(parameter => parameter.ParameterType));
        Assert.False(operation.GetParameters()[0].IsOptional);
        Assert.False(operation.GetParameters()[1].IsOptional);
        Assert.True(operation.GetParameters()[2].IsOptional);
        Assert.Null(typeof(IBpmnDiagramPresentation).GetMethod(operation.Name));
    }

    [Fact]
    public async Task SmartActionReactivationValidatesArgumentsAndDetachedCancellationThroughPublicModeler()
    {
        using var component = new InceptusBpmnModeler();
        var stamp = CreateApplicationActionContext().Stamp;
        await Assert.ThrowsAsync<ArgumentNullException>(() => component.RequestApplicationContextualActionsAsync(null!, 0).AsTask());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => component.RequestApplicationContextualActionsAsync(stamp, -1).AsTask());
        var unavailable = await component.RequestApplicationContextualActionsAsync(stamp, 0);
        Assert.Equal(BpmnModelerOperationStatus.Unavailable, unavailable.Status);
        Assert.False(unavailable.Presentation.IsAvailable);
        Assert.Equal("BPMN_PRESENTATION_UNAVAILABLE", Assert.Single(unavailable.Diagnostics).Code);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await component.RequestApplicationContextualActionsAsync(stamp, 0, cancellation.Token);
        Assert.Equal(BpmnModelerOperationStatus.Cancelled, cancelled.Status);
        Assert.Empty(cancelled.Diagnostics);
        component.Dispose();
        Assert.Equal(BpmnModelerOperationStatus.Unavailable,
            (await component.RequestApplicationContextualActionsAsync(stamp, 0)).Status);
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task SmartActionPolicyAndToolbarUpdatesPreserveProviderWrapperAndComponentIdentity(EditingSessionAccessMode mode)
    {
        using var services = CreateServices();
        await using var renderer = new ExceptionRecordingRenderer(services);
        var calls = 0;
        BpmnApplicationActionProvider provider = (_, _) =>
        {
            Assert.True(renderer.Dispatcher.CheckAccess());
            calls++;
            return ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([new("app:inspect", "Inspect")]);
        };
        BpmnApplicationActionHandler firstHandler = _ => ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Succeeded));
        BpmnApplicationActionHandler secondHandler = _ => ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Cancelled));
        await renderer.InitializeAsync(Parameters(
            (nameof(InceptusBpmnModeler.AccessMode), mode),
            (nameof(InceptusBpmnModeler.ApplicationActionProvider), provider),
            (nameof(InceptusBpmnModeler.ApplicationActionHandler), firstHandler)));
        var component = renderer.Component;
        var facade = GetFacade(component);
        var wrapper = ConfiguredApplicationActionProvider(component);
        foreach (var enabled in new[] { true, false, true })
        {
            await renderer.SetParametersAsync(Parameters(
                (nameof(InceptusBpmnModeler.ApplicationContextualActionsEnabled), enabled),
                (nameof(InceptusBpmnModeler.ApplicationActionsRevision), 7L)));
            Assert.Same(component, renderer.Component);
            Assert.Same(facade, GetFacade(component));
            Assert.Same(wrapper, ConfiguredApplicationActionProvider(component));
            Assert.Equal(enabled, ReadActionFacadeField<bool>(facade, "_applicationContextualActionsEnabled"));
            Assert.Equal(7, ReadActionFacadeField<long>(facade, "_applicationActionsRevision"));
            Assert.Equal(mode, component.AccessMode);
        }
        await renderer.SetParametersAsync(Parameters((nameof(InceptusBpmnModeler.ApplicationActionHandler), secondHandler)));
        Assert.Same(wrapper, ConfiguredApplicationActionProvider(component));
        Assert.Same(secondHandler, ReadActionFacadeField<BpmnApplicationActionHandler>(facade, "_applicationActionAuthority"));
        Assert.True(ReadActionFacadeField<bool>(facade, "_applicationTypedHandlerAvailable"));
        Assert.False(ReadActionFacadeField<bool>(facade, "_applicationActionCallbackAvailable"));
        Assert.Equal(0, calls); // Policy parameter changes alone do not manufacture a selection/discovery request.
        Assert.Equal("app:inspect", Assert.Single(await wrapper(CreateApplicationActionContext(), CancellationToken.None)).Id);
        Assert.Equal(1, calls);
        BpmnApplicationActionProvider replacement = (_, _) => ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([]);
        await renderer.SetParametersAsync(Parameters((nameof(InceptusBpmnModeler.ApplicationActionProvider), replacement)));
        Assert.NotSame(wrapper, ConfiguredApplicationActionProvider(component));
        Assert.Same(facade, GetFacade(component));
        Assert.Empty(renderer.Exceptions);
    }

    [Fact]
    public void SmartActionContextCopiesApplicationRevisionWithoutInventingSelectionMembershipOrPointerHit()
    {
        var previous = CreateApplicationActionContext();
        var presentation = new BpmnPresentationSnapshot(previous.Stamp, [], null, [], [], []);
        var request = new BpmnApplicationActionContext(presentation, previous.Target, null, 19, 37);
        Assert.Equal(37, request.ApplicationActionsRevision);
        Assert.Equal(19, request.RequestVersion);
        Assert.Empty(request.Presentation.Selection);
        Assert.Same(previous.Target, request.Target);
        Assert.Null(request.Part);
        Assert.Same(previous.Stamp, request.Stamp);
        Assert.Equal(0, previous.ApplicationActionsRevision);
        Assert.Null(typeof(BpmnApplicationActionContext).GetProperty(nameof(request.ApplicationActionsRevision))!.SetMethod);
    }

    private static T ReadActionFacadeField<T>(BpmnModelerFacade facade, string name) =>
        Assert.IsType<T>(typeof(BpmnModelerFacade).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(facade));
}
