import React, { useState } from 'react';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import TableCell from 'Components/Table/TableCell';
import Button from 'Components/Button';
import translate from 'Utilities/String/translate';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';

// Mocked UI for unmapped files since no backend API controller was provided in the spec
function UnmappedTable() {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedFolder, setSelectedFolder] = useState<string | undefined>(undefined);

  const mockFiles = [
    { id: 1, path: '/watch/Some.Unmapped.Show.S01E01.mkv', status: 'Unmapped' }
  ];

  const handleManualImport = (path: string) => {
    setSelectedFolder(path);
    setIsModalOpen(true);
  };

  const columns = [
    { key: 'path', label: 'File Path' },
    { key: 'status', label: 'Status' },
    { key: 'actions', label: 'Actions' }
  ];

  return (
    <div>
      <Table columns={columns}>
        <TableBody>
          {mockFiles.map(file => (
            <TableRow key={file.id}>
              <TableCell>{file.path}</TableCell>
              <TableCell>{file.status}</TableCell>
              <TableCell>
                <Button onClick={() => handleManualImport(file.path)}>
                  {translate('Manual Import')}
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {isModalOpen && (
        <InteractiveImportModal
          isOpen={isModalOpen}
          onModalClose={() => setIsModalOpen(false)}
          folder={selectedFolder}
        />
      )}
    </div>
  );
}

export default UnmappedTable;
