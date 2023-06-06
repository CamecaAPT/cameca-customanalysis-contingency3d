using Cameca.CustomAnalysis.Interface;
using Cameca.CustomAnalysis.Utilities;
using Cameca.CustomAnalysis.Utilities.Legacy;

namespace Cameca.CustomAnalysis.ContingencyTable3D.Core;

[DefaultView(ContingencyTable3DViewModel.UniqueId, typeof(ContingencyTable3DViewModel))]
internal class ContingencyTable3DNode : LegacyCustomAnalysisNodeBase<ContingencyTable3DAnalysis, ContingencyTable3DOptions>
{
    public const string UniqueId = "Cameca.CustomAnalysis.ContingencyTable3D.ContingencyTable3DNode";
    
    public static INodeDisplayInfo DisplayInfo { get; } = new NodeDisplayInfo("3D Contingency Table");

    public ContingencyTable3DNode(IStandardAnalysisNodeBaseServices services, ContingencyTable3DAnalysis analysis)
        : base(services, analysis)
    {
    }
}