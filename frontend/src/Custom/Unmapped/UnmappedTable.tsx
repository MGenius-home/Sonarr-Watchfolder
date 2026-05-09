import React, { useState } from 'react';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Button from 'Components/Link/Button';
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
    { name: 'path', label: 'File Path', isVisible: true },
    { name: 'status', label: 'Status', isVisible: true },
    { name: 'actions', label: 'Actions', isVisible: true }
  ];

  return (
    <div>
      <Table columns={columns}>
        <TableBody>
          {mockFiles.map(file => (
            <TableRow key={file.id}>
              <TableRowCell>{file.path}</TableRowCell>
              <TableRowCell>{file.status}</TableRowCell>
              <TableRowCell>
                <Button onClick={() => handleManualImport(file.path)}>
                  {translate('ManualImport')}
                </Button>
              </TableRowCell>
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
