import ModelBase from 'App/ModelBase';

interface RootFolder extends ModelBase {
  id: number;
  name: string;
  path: string;
  folderType: number;
  accessible: boolean;
  freeSpace?: number;
  unmappedFolders: object[];
}

export default RootFolder;
