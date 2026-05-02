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
      selectedState: {},
      // recordingInputs holds user-typed IDs for rows that have no tag value
      // keyed by item.id
      recordingInputs: {}
    };
  }

  componentDidUpdate(prevProps) {
    const { items } = this.props;

    if (prevProps.items !== items) {
      this.resetSelectedState(items);
    }
  }

  resetSelectedState(items) {
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

  getImportableSelectedIds = () => {
    const { items } = this.props;

    return this.getSelectedIds().filter((id) => {
      const item = items.find((i) => i.id === id);

      return item &&
        item.artist &&
        item.album &&
        item.tracks &&
        item.tracks.length > 0 &&
        (!item.rejections || item.rejections.length === 0);
    });
  };

  // IDs of selected items whose source file already exists in the library.
  // These can be cleaned up from the import folder without a full import.
  getDeletableSelectedIds = () => {
    const { items } = this.props;

    return this.getSelectedIds().filter((id) => {
      const item = items.find((i) => i.id === id);

      return item && item.hasExistingFiles;
    });
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
    const importableIds = this.getImportableSelectedIds();

    if (importableIds.length > 0) {
      this.props.onImportPress(importableIds);
    }
  };

  onDeleteDuplicatesPress = () => {
    const deletableIds = this.getDeletableSelectedIds();

    if (deletableIds.length > 0) {
      this.props.onDeleteDuplicatesPress(deletableIds);
    }
  };

  onRecordingInputChange = (itemId, value) => {
    this.setState((state) => ({
      recordingInputs: {
        ...state.recordingInputs,
        [itemId]: value
      }
    }));
  };

  onLookupPress = (item) => {
    // Prefer the tag value if it exists, otherwise use what the user typed
    const tagId = item.audioTags && item.audioTags.recordingMBId;
    const typedId = this.state.recordingInputs[item.id] || '';
    const recordingId = (tagId || typedId).trim();

    if (!recordingId) {
      return;
    }

    this.props.onLookupRecording(item.id, recordingId);
  };

  onLookupKeyDown = (item, e) => {
    if (e.key === 'Enter') {
      this.onLookupPress(item);
    }
  };

  //
  // Render helpers

  renderRecordingIdCell(item) {
    const tagId = item.audioTags && item.audioTags.recordingMBId;
    const typedId = this.state.recordingInputs[item.id] || '';
    const isLookingUp = !!item.isLookingUp;
    const lookupError = item.lookupError || null;

    // The tag value is read-only — it came from the file itself
    if (tagId) {
      return (
        <div className={styles.recordingCell}>
          <span className={styles.recordingTagId} title="Read from file tags">
            {tagId}
          </span>
          <button
            className={styles.lookupBtn}
            disabled={isLookingUp}
            title="Search library for this recording ID"
            onClick={() => this.onLookupPress(item)}
          >
            {isLookingUp ?
              <Icon name={icons.SPINNER} isSpinning={true} size={12} /> :
              <Icon name={icons.SEARCH} size={12} />
            }
          </button>
          {lookupError && (
            <span className={styles.lookupError} title={lookupError}>
              <Icon name={icons.WARNING} kind={kinds.WARNING} size={12} />
            </span>
          )}
        </div>
      );
    }

    // No tag — show an editable input so the user can supply a recording ID
    return (
      <div className={styles.recordingCell}>
        <input
          className={styles.recordingInput}
          type="text"
          placeholder="Enter MusicBrainz recording ID…"
          value={typedId}
          disabled={isLookingUp}
          onChange={(e) => this.onRecordingInputChange(item.id, e.target.value)}
          onKeyDown={(e) => this.onLookupKeyDown(item, e)}
        />
        <button
          className={styles.lookupBtn}
          disabled={isLookingUp || !typedId.trim()}
          title="Search library for this recording ID"
          onClick={() => this.onLookupPress(item)}
        >
          {isLookingUp ?
            <Icon name={icons.SPINNER} isSpinning={true} size={12} /> :
            <Icon name={icons.SEARCH} size={12} />
          }
        </button>
        {lookupError && (
          <span className={styles.lookupError} title={lookupError}>
            <Icon name={icons.WARNING} kind={kinds.WARNING} size={12} />
            {' '}<span className={styles.lookupErrorText}>{lookupError}</span>
          </span>
        )}
      </div>
    );
  }

  renderStatus(item) {
    const { rejections, hasExistingFiles } = item;

    if (!item.artist) {
      return <span className={styles.noMatch}>No artist match</span>;
    }

    if (!item.album) {
      return <span className={styles.noMatch}>No album match</span>;
    }

    if (!item.tracks || item.tracks.length === 0) {
      return <span className={styles.noMatch}>No track match</span>;
    }

    // Show "Already in library" before rejections: a quality-based rejection
    // (e.g. "Not an upgrade") IS the duplicate situation and shouldn't hide it.
    if (hasExistingFiles) {
      return (
        <span
          className={styles.duplicate}
          title="Already in your library — select and click Delete Duplicates to remove the import copy, or Import to replace it if the quality is better."
        >
          Already in library
        </span>
      );
    }

    if (rejections && rejections.length > 0) {
      return (
        <span className={styles.rejections}>
          {rejections.map((r, i) => (
            <span key={i} className={styles.rejection}>
              {r.message}
            </span>
          ))}
        </span>
      );
    }

    return <span className={styles.readyToImport}>Ready to import</span>;
  }

  renderTrackInfo(item) {
    const { tracks } = item;

    if (!tracks || tracks.length === 0) {
      return <span className={styles.noMatch}>—</span>;
    }

    if (tracks.length === 1) {
      return (
        <span title={tracks[0].title}>
          #{tracks[0].trackNumber} {tracks[0].title}
        </span>
      );
    }

    return (
      <span title={tracks.map((t) => `#${t.trackNumber} ${t.title}`).join('\n')}>
        {tracks.length} tracks
      </span>
    );
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

    const importableSelectedIds = this.getImportableSelectedIds();
    const deletableSelectedIds = this.getDeletableSelectedIds();
    const canImport = importableSelectedIds.length > 0 && !isSaving;
    const canDeleteDuplicates = deletableSelectedIds.length > 0 && !isSaving;

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
              label={translate('ImportMoveRename')}
              iconName={icons.INTERACTIVE}
              isDisabled={!canImport}
              isSpinning={isSaving}
              onPress={this.onImportPress}
            />

            <PageToolbarButton
              label="Delete Duplicates"
              iconName={icons.DELETE}
              isDisabled={!canDeleteDuplicates}
              isSpinning={isSaving}
              onPress={this.onDeleteDuplicatesPress}
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

          {importFolder && isFetching && !isPopulated && <LoadingIndicator />}

          {
            importFolder && !isFetching && error &&
              <Alert kind={kinds.DANGER}>
                Failed to load files from import folder: {importFolder.path}
              </Alert>
          }

          {
            importFolder && isPopulated && !error && items.length === 0 &&
              <Alert kind={kinds.INFO}>
                No files found in import folder: <strong>{importFolder.path}</strong>
              </Alert>
          }

          {
            importFolder && isPopulated && !error && items.length > 0 &&
              <div className={styles.tableContainer}>
                <div className={styles.importFolderPath}>
                  <Icon name={icons.FOLDER} />
                  {' '}Import Folder: <strong>{importFolder.path}</strong>
                  {' '}— {items.length} file{items.length !== 1 ? 's' : ''} found
                </div>

                {
                  isSaving &&
                    <Alert kind={kinds.INFO} className={styles.savingAlert}>
                      <Icon name={icons.SPINNER} isSpinning={true} />
                      {' '}Importing, moving and renaming files… this may take a moment.
                    </Alert>
                }

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
                      <th className={styles.recordingIdCell}>Recording ID</th>
                      <th className={styles.artistCell}>Artist</th>
                      <th className={styles.albumCell}>Album</th>
                      <th className={styles.tracksCell}>Track</th>
                      <th className={styles.qualityCell}>Quality</th>
                      <th className={styles.statusCell}>Status</th>
                    </tr>
                  </thead>
                  <tbody>
                    {items.map((item) => {
                      const isSelected = !!selectedState[item.id];
                      const isImportable = item.artist &&
                        item.album &&
                        item.tracks &&
                        item.tracks.length > 0 &&
                        (!item.rejections || item.rejections.length === 0);

                      // Duplicate items (already in library) are selectable even when
                      // isImportable is false (e.g. quality not an upgrade) so the user
                      // can select them for "Delete Duplicates".
                      const isSelectable = isImportable || !!item.hasExistingFiles;

                      const rowClass = item.hasExistingFiles
                        ? styles.duplicateRow
                        : !isImportable
                          ? styles.invalidRow
                          : styles.validRow;

                      return (
                        <tr
                          key={item.id}
                          className={rowClass}
                        >
                          <td className={styles.selectCell}>
                            <input
                              type="checkbox"
                              checked={isSelected}
                              disabled={!isSelectable}
                              onChange={(e) => this.onSelectedChange({
                                id: item.id,
                                value: e.target.checked
                              })}
                            />
                          </td>
                          <td className={styles.pathCell} title={item.path}>
                            {item.name || item.path.split('/').pop()}
                          </td>
                          <td className={styles.recordingIdCell}>
                            {this.renderRecordingIdCell(item)}
                          </td>
                          <td className={styles.artistCell}>
                            {item.artist ?
                              item.artist.artistName :
                              <span className={styles.noMatch}>Unknown</span>
                            }
                          </td>
                          <td className={styles.albumCell}>
                            {item.album ?
                              item.album.title :
                              <span className={styles.noMatch}>Unknown</span>
                            }
                          </td>
                          <td className={styles.tracksCell}>
                            {this.renderTrackInfo(item)}
                          </td>
                          <td className={styles.qualityCell}>
                            {item.quality ? item.quality.quality.name : '—'}
                          </td>
                          <td className={styles.statusCell}>
                            {this.renderStatus(item)}
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>

                <div className={styles.tableFooter}>
                  <span>
                    {(() => {
                      const hasAnySelection = importableSelectedIds.length > 0 || deletableSelectedIds.length > 0;

                      if (!hasAnySelection) {
                        return 'Select files to import or delete — only fully matched files can be imported';
                      }

                      const importableItems = importableSelectedIds.map((id) => items.find((i) => i.id === id)).filter(Boolean);
                      const newCount = importableItems.filter((i) => !i.hasExistingFiles).length;
                      // Duplicates that are also importable (will be cleaned up via Import)
                      const dupViaImport = importableItems.filter((i) => i.hasExistingFiles).length;
                      // Duplicates selected that are NOT importable (unmatched but flagged) — Delete Duplicates only
                      const dupOnlyCount = deletableSelectedIds.filter((id) => !importableSelectedIds.includes(id)).length;
                      const totalDupCount = dupViaImport + dupOnlyCount;

                      const parts = [];

                      if (newCount > 0) {
                        parts.push(`${newCount} new file${newCount !== 1 ? 's' : ''} to import`);
                      }

                      if (totalDupCount > 0) {
                        parts.push(`${totalDupCount} duplicate${totalDupCount !== 1 ? 's' : ''} to delete`);
                      }

                      return parts.join(', ');
                    })()}
                  </span>
                  <span className={styles.importNote}>
                    Files will be <strong>moved</strong> and renamed per your naming settings
                  </span>
                </div>
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
  onImportPress: PropTypes.func.isRequired,
  onDeleteDuplicatesPress: PropTypes.func.isRequired,
  onLookupRecording: PropTypes.func.isRequired
};

MusicImportPage.defaultProps = {
  items: []
};

export default MusicImportPage;
