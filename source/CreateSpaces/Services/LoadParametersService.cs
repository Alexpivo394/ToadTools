using CreateSpaces.Models;

namespace CreateSpaces.Services;

public class LoadParametersService
{
    private GetParameterService _getParameterService;
    private RevitRoomProvider _roomProvider;

    public LoadParametersService(GetParameterService getParameterService, RevitRoomProvider roomProvider)
    {
        _getParameterService = getParameterService;
        _roomProvider = roomProvider;
    }

    public IReadOnlyList<ParameterDescriptor> GetRoomParameters()
    {
        var detect = _roomProvider.DetectRooms();
        if (detect == false)
            return Array.Empty<ParameterDescriptor>();
        var room = _roomProvider.GetRoomsFromLink()?.FirstOrDefault();
        var roomParameters = _getParameterService.GetFromRoom(room);
        
        return roomParameters;
    }
    
    public IReadOnlyList<ParameterDescriptor> GetSpaceParameters()
    {
        var spaceParameters = _getParameterService.GetFromTemporarySpace();
        return spaceParameters;
    }
}