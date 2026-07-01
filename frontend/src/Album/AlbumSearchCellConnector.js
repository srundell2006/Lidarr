import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import * as commandNames from 'Commands/commandNames';
import { executeCommand } from 'Store/Actions/commandActions';
import createArtistSelector from 'Store/Selectors/createArtistSelector';
import createCommandsSelector from 'Store/Selectors/createCommandsSelector';
import { isCommandExecuting } from 'Utilities/Command';
import AlbumSearchCell from './AlbumSearchCell';

function createMapStateToProps() {
  return createSelector(
    (state, { albumId }) => albumId,
    createArtistSelector(),
    createCommandsSelector(),
    (albumId, artist, commands) => {
      const isSearching = commands.some((command) => {
        const albumSearch = command.name === commandNames.ALBUM_SEARCH;

        if (!albumSearch) {
          return false;
        }

        return (
          isCommandExecuting(command) &&
          command.body.albumIds.indexOf(albumId) > -1
        );
      });

      const isScanning = commands.some((command) => {
        return (
          command.name === commandNames.SCAN_ALBUM &&
          isCommandExecuting(command) &&
          command.body.albumId === albumId
        );
      });

      return {
        artistMonitored: artist.monitored,
        artistType: artist.artistType,
        isSearching,
        isScanning
      };
    }
  );
}

function createMapDispatchToProps(dispatch, props) {
  return {
    onSearchPress() {
      dispatch(executeCommand({
        name: commandNames.ALBUM_SEARCH,
        albumIds: [props.albumId]
      }));
    },
    onScanPress() {
      dispatch(executeCommand({
        name: commandNames.SCAN_ALBUM,
        albumId: props.albumId
      }));
    }
  };
}

export default connect(createMapStateToProps, createMapDispatchToProps)(AlbumSearchCell);
