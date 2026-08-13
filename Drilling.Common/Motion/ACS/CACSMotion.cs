using Drilling.Common.Managers;
using Drilling.Common.Interface;
using Drilling.Common.Motion;
using Drilling.Common.Alarm;
using Drilling.Common.InterLock;
using Drilling.Common.Station;

namespace Drilling.Common.Motion;

[CMotionControllerType("ACS", "ACS.NET", "SPIIPLUS")]
internal sealed class CACSMotion(IInterfaceManager? interfaceManager, int deviceNo = 0)
    : CMotionController("ACS", interfaceManager, deviceNo)
{
    protected override string CommandPrefix => "ACS";
}

