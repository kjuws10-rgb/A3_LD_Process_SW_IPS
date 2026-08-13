using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CMotionControllerType("AJIN")]
internal sealed class CAjinMotion(IInterfaceManager? interfaceManager, int deviceNo = 0)
    : CMotionController("AJIN", interfaceManager, deviceNo)
{
    protected override string CommandPrefix => "AJIN";
}

