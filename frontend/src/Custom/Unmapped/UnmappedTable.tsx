import React, { useState } from 'react';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Button from 'Components/Link/Button';
import translate from 'Utilities/String/translate';
import InteractiveImportModal from 'InteractiveImport/InteractiveImportModal';

import useApiQuery from 'Helpers/Hooks/useApiQuery';

// UI for unmapped files connected to the real backend API
function UnmappedTable() {
  const [isModalOpen, setIsModalOpen] = useState(false);
  const [selectedFolder, setSelectedFolder] = useState<string | undefined>(undefined);

  const { data: files, isLoading } = useApiQuery<any[]>({
    path: '/localwatchbuffer'
  });

  const handleManualImport = (path: string) => {
    setSelectedFolder(path);
    setIsModalOpen(true);
  };

  const columns = [
    { name: 'path', label: 'File Path', isVisible: true },
    { name: 'status', label: 'Status', isVisible: true },
    { name: 'actions', label: 'Actions', isVisible: true }
  ];

  if (isLoading) {
    return <div>Loading...</div>;
  }

  return (
    <div>
      <Table columns={columns}>
        <TableBody>
          {(files || []).map((file: any) => (
            <TableRow key={file.id}>
              <TableRowCell>{file.path}</TableRowCell>
              <TableRowCell>Unmapped</TableRowCell>
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
