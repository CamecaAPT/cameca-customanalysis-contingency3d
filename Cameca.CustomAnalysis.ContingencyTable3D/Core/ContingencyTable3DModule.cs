using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;
using Prism.Ioc;
using Prism.Modularity;

namespace Cameca.CustomAnalysis.ContingencyTable3D.Core;

/// <summary>
/// Public <see cref="IModule"/> implementation is the entry point for AP Suite to discover and configure the custom analysis
/// </summary>
public class ContingencyTable3DModule : IModule
{
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
#pragma warning disable CS0618 // Type or member is obsolete
        containerRegistry.AddCustomAnalysisUtilities(options => options.UseLegacy = true);
#pragma warning restore CS0618 // Type or member is obsolete

        containerRegistry.Register<ContingencyTable3DAnalysis>();
        containerRegistry.Register<object, ContingencyTable3DNode>(ContingencyTable3DNode.UniqueId);
        containerRegistry.RegisterInstance(ContingencyTable3DNode.DisplayInfo, ContingencyTable3DNode.UniqueId);
        containerRegistry.Register<IAnalysisMenuFactory, ContingencyTable3DNodeMenuFactory>(nameof(ContingencyTable3DNodeMenuFactory));
        containerRegistry.Register<object, ContingencyTable3DViewModel>(ContingencyTable3DViewModel.UniqueId);
    }

    public void OnInitialized(IContainerProvider containerProvider)
    {
        var extensionRegistry = containerProvider.Resolve<IExtensionRegistry>();
        extensionRegistry.RegisterAnalysisView<LegacyCustomAnalysisView, ContingencyTable3DViewModel>(AnalysisViewLocation.Top);
    }
}
