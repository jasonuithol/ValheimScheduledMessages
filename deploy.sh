TARGET="/home/jason/.steam/steam/steamapps/common/Valheim dedicated server"
cp bin/Release/netstandard2.1/ScheduledMessages.dll "${TARGET}"/BepInEx/plugins/
cp scheduledmessages.cfg "${TARGET}"/BepInEx/config/
echo Files deployed to Valheim Server BepInEx plugin and config folders.
