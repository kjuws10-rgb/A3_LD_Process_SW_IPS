using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CMotionControllerType("A3200")]
internal sealed class CA3200Motion(IInterfaceManager? interfaceManager, int deviceNo = 0)
    : CMotionController("A3200", interfaceManager, deviceNo)
{
    protected override string CommandPrefix => "A3200";
}

