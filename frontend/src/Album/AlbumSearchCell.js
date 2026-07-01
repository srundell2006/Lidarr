import PropTypes from 'prop-types';
import React, { Component } from 'react';
import IconButton from 'Components/Link/IconButton';
import SpinnerIconButton from 'Components/Link/SpinnerIconButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import AlbumInteractiveSearchModalConnector from './Search/AlbumInteractiveSearchModalConnector';
import styles from './AlbumSearchCell.css';

class AlbumSearchCell extends Component {

  //
  // Lifecycle

  constructor(props, context) {
    super(props, context);

    this.state = {
      isDetailsModalOpen: false
    };
  }

  //
  // Listeners

  onManualSearchPress = () => {
    this.setState({ isDetailsModalOpen: true });
  };

  onDetailsModalClose = () => {
    this.setState({ isDetailsModalOpen: false });
  };

  //
  // Render

  render() {
    const {
      albumId,
      albumTitle,
      isSearching,
      isScanning,
      onSearchPress,
      onScanPress,
      ...otherProps
    } = this.props;

    return (
      <TableRowCell className={styles.AlbumSearchCell}>
        <SpinnerIconButton
          name={icons.SEARCH}
          isSpinning={isSearching}
          onPress={onSearchPress}
          title={translate('AutomaticSearch')}
        />

        <IconButton
          name={icons.INTERACTIVE}
          onPress={this.onManualSearchPress}
          title={translate('InteractiveSearch')}
        />

        <SpinnerIconButton
          name={icons.RESCAN}
          isSpinning={isScanning}
          onPress={onScanPress}
          title={translate('ScanAlbumFiles')}
        />

        <AlbumInteractiveSearchModalConnector
          isOpen={this.state.isDetailsModalOpen}
          albumId={albumId}
          albumTitle={albumTitle}
          onModalClose={this.onDetailsModalClose}
          {...otherProps}
        />

      </TableRowCell>
    );
  }
}

AlbumSearchCell.propTypes = {
  albumId: PropTypes.number.isRequired,
  artistId: PropTypes.number.isRequired,
  albumTitle: PropTypes.string.isRequired,
  isSearching: PropTypes.bool.isRequired,
  isScanning: PropTypes.bool.isRequired,
  onSearchPress: PropTypes.func.isRequired,
  onScanPress: PropTypes.func.isRequired
};

export default AlbumSearchCell;
