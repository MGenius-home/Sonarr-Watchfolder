import React from 'react';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import TableRowCell from 'Components/Table/Cells/TableRowCell';

import useApiQuery from 'Helpers/Hooks/useApiQuery';

function RemoteSyncTable() {
  const { data: rows, isLoading } = useApiQuery<any[]>({
    path: '/remotesync'
  });

  const columns = [
    { name: 'syncedAt', label: 'When', isVisible: true },
    { name: 'title', label: 'Title', isVisible: true },
    { name: 'action', label: 'Action', isVisible: true },
    { name: 'detail', label: 'Detail', isVisible: true }
  ];

  if (isLoading) {
    return <div>Loading...</div>;
  }

  return (
    <Table columns={columns}>
      <TableBody>
        {(rows || []).map((row: any) => (
          <TableRow key={row.id}>
            <TableRowCell>{new Date(row.syncedAt).toLocaleString()}</TableRowCell>
            <TableRowCell>{row.title}</TableRowCell>
            <TableRowCell>{row.action}</TableRowCell>
            <TableRowCell>{row.detail}</TableRowCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default RemoteSyncTable;
