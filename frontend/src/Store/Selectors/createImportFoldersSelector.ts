import { createSelector } from 'reselect';
import { ImportFolderAppState } from 'App/State/SettingsAppState';
import createSortedSectionSelector from 'Store/Selectors/createSortedSectionSelector';
import RootFolder from 'typings/RootFolder';
import sortByProp from 'Utilities/Array/sortByProp';

export default function createImportFoldersSelector() {
  return createSelector(
    createSortedSectionSelector<RootFolder>(
      'settings.importFolders',
      sortByProp('name')
    ),
    (importFolders: ImportFolderAppState) => importFolders
  );
}
