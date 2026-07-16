import _ from 'lodash';
import PropTypes from 'prop-types';
import React, { Component } from 'react';
import { connect } from 'react-redux';
import { createSelector } from 'reselect';
import { saveArtist, setArtistValue } from 'Store/Actions/artistActions';
import createArtistSelector from 'Store/Selectors/createArtistSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import EditArtistModalContent from './EditArtistModalContent';

function createIsPathChangingSelector() {
  return createSelector(
    (state) => state.artist.pendingChanges,
    createArtistSelector(),
    (pendingChanges, artist) => {
      const path = pendingChanges.path;

      if (path == null) {
        return false;
      }

      return artist.path !== path;
    }
  );
}

function createMapStateToProps() {
  return createSelector(
    (state) => state.artist,
    (state) => state.settings.metadataProfiles,
    createArtistSelector(),
    createIsPathChangingSelector(),
    (artistState, metadataProfiles, artist, isPathChanging) => {
      const {
        isSaving,
        saveError,
        pendingChanges
      } = artistState;

      const artistSettings = _.pick(artist, [
        'monitored',
        'monitorNewItems',
        'qualityProfileId',
        'metadataProfileId',
        'path',
        'tags',
        'artistName'
      ]);

      const settings = selectSettings(artistSettings, pendingChanges, saveError);

      // Build the list of selectable names: current name + all aliases, deduplicated.
      const aliases = artist.artistAliases || [];
      const allNames = _.uniq([artist.artistName, ...aliases].filter(Boolean)).sort();
      const nameOptions = allNames.map((name) => ({ key: name, value: name }));

      return {
        artistName: artist.artistName,
        isSaving,
        saveError,
        isPathChanging,
        originalPath: artist.path,
        item: settings.settings,
        nameOptions,
        showMetadataProfile: metadataProfiles.items.length > 1,
        ...settings
      };
    }
  );
}

const mapDispatchToProps = {
  dispatchSetArtistValue: setArtistValue,
  dispatchSaveArtist: saveArtist
};

class EditArtistModalContentConnector extends Component {

  //
  // Lifecycle

  componentDidUpdate(prevProps, prevState) {
    if (prevProps.isSaving && !this.props.isSaving && !this.props.saveError) {
      this.props.onModalClose();
    }
  }

  //
  // Listeners

  onInputChange = ({ name, value }) => {
    this.props.dispatchSetArtistValue({ name, value });
  };

  onSavePress = (moveFiles) => {
    this.props.dispatchSaveArtist({
      id: this.props.artistId,
      moveFiles
    });
  };

  //
  // Render

  render() {
    return (
      <EditArtistModalContent
        {...this.props}
        onInputChange={this.onInputChange}
        onSavePress={this.onSavePress}
        onMoveArtistPress={this.onMoveArtistPress}
      />
    );
  }
}

EditArtistModalContentConnector.propTypes = {
  artistId: PropTypes.number,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  nameOptions: PropTypes.arrayOf(PropTypes.object),
  dispatchSetArtistValue: PropTypes.func.isRequired,
  dispatchSaveArtist: PropTypes.func.isRequired,
  onModalClose: PropTypes.func.isRequired
};

export default connect(createMapStateToProps, mapDispatchToProps)(EditArtistModalContentConnector);
