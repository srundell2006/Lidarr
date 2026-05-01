import PropTypes from 'prop-types';
import React, { Component } from 'react';
import Alert from 'Components/Alert';
import Icon from 'Components/Icon';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import selectAll from 'Utilities/Table/selectAll';
import toggleSelected from 'Utilities/Table/toggleSelected';
import styles from './MusicImportPage.css';

class MusicImportPage extends Component {

  constructor(props, context) {
    super(props, context);

    this.state = {
      allSelected: false,
      allUnselected: true,
      selectedState: {}
    };
  }

  componentDidUpdate(prevProps) {
    const { items } = this.props;

    if (prevProps.items !== items) {
      this.setSelectedState(items);
    }
  }

  setSelectedState(items) {
    const newSelectedState = {};

    items.forEach((item) => {
      newSelectedState[item.id] = false;
    });

    this.setState({
      selectedState: newSelectedState,
      allSelected: false,
      allUnselected: true
    });
  }

  getSelectedIds = () => {
    return getSelectedIds(this.state.selectedState);
  };

  onSelectAllChange = ({ value }) => {
    this.setState(selectAll(this.state.selectedState, value));
  };

  onSelectedChange = ({ id, value, shiftKey = false }) => {
    this.setState((state) => {
      return toggleSelected(state, this.props.items, id, value, shiftKey);
    });
  };

  onImportPress = () => {
    const selectedIds = this.getSelectedIds();
    if (selectedIds.length > 0) {
      this.props.onImportPress(selectedIds);
    }
  };

  renderRejections(rejections) {
    if (!rejections || rejections.length === 0) {
      return <Icon name={icons.CHECK} kind={kinds.SUCCESS} />;
    }

    return (
      <span className={styles.rejections}>
        {rejections.map((r, i) => (
          <span key={i} className={styles.rejection} title={r.message}>
            <Icon name={icons.DANGER} kind={kinds.DANGER} />
            {' '}{r.message}
          </span>
        ))}
      </span>
    );
  }

  renderTrackCount(tracks) {
    if (!tracks || tracks.length === 0) {
      return <span className={styles.noMatch}>—</span>;
    }
    return <span>{tracks.length} track{tracks.length !== 1 ? 's' : ''}</span>;
  }

  render() {
    const {
      importFolder,
      isFetching,
      isPopulated,
      isSaving,
      error,
      items,
      onRefreshPress
    } = this.props;

    const {
      allSelected,
      allUnselected,
      selectedState
    } = this.state;

    const selectedIds = this.getSelectedIds();
    const importableIds = selectedIds.filter((id) => {
      const item = items.find((i) => i.id === id);
      return item && (!item.rejections || item.rejections.length === 0);
    });

    return (
      <PageContent title={translate('MusicImport')}>
        <PageToolbar>
          <PageToolbarSection>
            <PageToolbarButton
              label={translate('Refresh')}
              iconName={icons.REFRESH}
              isSpinning={isFetching}
              isDisabled={!importFolder}
              onPress={onRefreshPress}
            />

            <PageToolbarButton
              label={translate('Import')}
              iconName={icons.DOWNLOAD}
              isDisabled={importableIds.length === 0 || isSaving}
              isSpinning={isSaving}
              onPress={this.onImportPress}
            />
          </PageToolbarSection>
        </PageToolbar>

        <PageContentBody>
          {
            !importFolder &&
              <Alert kind={kinds.WARNING}>
                No Import Folder configured. Go to{' '}
                <a href="/settings/mediamanagement">Settings &rsaquo; Media Management</a>{' '}
                and add an Import Folder first.
              </Alert>
          }

          {
            importFolder && isFetching && !isPopulated &&
              <LoadingIndicator />
          }

          {
            importFolder && !isFetching && error &&
              <Alert kind={kinds.DANGER}>
                Failed to load files from import folder: {importFolder.path}
              </Alert>
          }

          {
            importFolder && isPopulated && !error && items.length === 0 &&
              <Alert kind={kinds.INFO}>
                No files found in import folder: {importFolder.path}
              </Alert>
          }

          {
            importFolder && isPopulated && !error && items.length > 0 &&
              <div className={styles.tableContainer}>
                <div className={styles.importFolderPath}>
                  Import Folder: <strong>{importFolder.path}</strong>
                  {' '}— {items.length} file{items.length !== 1 ? 's' : ''} found
                </div>

                <table className={styles.table}>
                  <thead>
                    <tr>
                      <th className={styles.selectCell}>
                        <input
                          type="checkbox"
                          checked={allSelected}
                          ref={(el) => {
                            if (el) {
                              el.indeterminate = !allSelected && !allUnselected;
                            }
                          }}
                          onChange={(e) => this.onSelectAllChange({ value: e.target.checked })}
                        />
                      </th>
                      <th className={styles.pathCell}>File</th>
                      <th className={styles.artistCell}>Artist</th>
                      <th className={styles.albumCell}>Album</th>
                      <th className={styles.tracksCell}>Tracks</th>
                      <th className={styles.qualityCell}>Quality</th>
                      <th className={styles.statusCell}>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((item) => {
                      const isSelected = !!selectedState[item.id];
                      const hasRejections = item.rejections && item.rejections.length > 0;

                      return (
                        <tr
                          key={item.id}
                          className={hasRejections ? styles.rejectedRow : styles.validRow}
                        >
                          <td className={styles.selectCell}>
                            <input
                              type="checkbox"
                              checked={isSelected}
                              disabled={hasRejections}
                              onChange={(e) => this.onSelectedChange({
                                id: item.id,
                                value: e.target.checked
                              })}
                            />
                          </td>
                          <td className={styles.pathCell} title={item.path}>
                            {item.name || item.path.split('/').pop()}
                          </td>
                          <td className={styles.artistCell}>
                            {item.artist ? item.artist.artistName : <span className={styles.noMatch}>Unknown</span>}
                          </td>
                          <td className={styles.albumCell}>
                            {item.album ? item.album.title : <span className={styles.noMatch}>Unknown</span>}
                          </td>
                          <td className={styles.tracksCell}>
                            {this.renderTrackCount(item.tracks)}
                          </td>
                          <td className={styles.qualityCell}>
                            {item.quality ? item.quality.quality.name : '—'}
                          </td>
                          <td className={styles.statusCell}>
                            {this.renderRejections(item.rejections)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>

                {
                  selectedIds.length > 0 && importableIds.length === 0 &&
                    <Alert kind={kinds.WARNING} className={styles.importAlert}>
                      All selected files have rejections and cannot be imported.
                    </Alert>
                }
              </div>
          }
        </PageContentBody>
      </PageContent>
    );
  }
}

MusicImportPage.propTypes = {
  importFolder: PropTypes.object,
  isFetching: PropTypes.bool.isRequired,
  isPopulated: PropTypes.bool.isRequired,
  isSaving: PropTypes.bool.isRequired,
  saveError: PropTypes.object,
  error: PropTypes.object,
  items: PropTypes.arrayOf(PropTypes.object).isRequired,
  onRefreshPress: PropTypes.func.isRequired,
  onImportPress: PropTypes.func.isRequired
};

MusicImportPage.defaultProps = {
  items: []
};

export default MusicImportPage;
